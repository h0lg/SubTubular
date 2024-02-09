namespace Ui

open System
open System.IO
open System.Runtime.CompilerServices
open System.Text.Json
open System.Threading
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Themes.Fluent
open Fabulous
open Fabulous.Avalonia
open type Fabulous.Avalonia.View
open FSharp.Control
open SubTubular
open SubTubular.Extensions
open Avalonia.Controls.Notifications

module App =
    type Scopes = videos = 0 | playlist = 1 | channel = 2
    type OpenOutputOptions = nothing = 0 | file = 1 | folder = 2

    type SavedSettings = {
        Top: float option
        CacheHours: float option

        OrderByScore: bool
        OrderDesc: bool
        Padding: int

        OutputHtml: bool
        OutputTo: string
        OpenOutput: OpenOutputOptions
    }

    type Model = {
        Scope: Scopes
        Aliases: string
        Query: string

        Top: float option
        CacheHours: float option

        OrderByScore: bool
        OrderDesc: bool
        Padding: int

        Searching: bool
        SearchResults: VideoSearchResult list

        DisplayOutputOptions: bool
        OutputHtml: bool
        OutputTo: string
        OpenOutput: OpenOutputOptions
    }

    type Msg =
        | ScopeChanged of SelectionChangedEventArgs
        | AliasesUpdated of string
        | QueryChanged of string

        | TopChanged of float option
        | CacheHoursChanged of float option

        | OrderByScoreChanged of bool
        | OrderDescChanged of bool
        | PaddingChanged of float option

        | DisplayOutputOptionsChanged of bool
        | OutputHtmlChanged of bool
        | OutputToChanged of string
        | OpenOutputChanged of SelectionChangedEventArgs
        | SaveOutput
        | SavedOutput of string

        | Search of bool
        | SearchResult of VideoSearchResult
        | SearchCompleted

        | Notify of string
        | CopyingToClipboard of RoutedEventArgs
        | OpenUrl of string
        | SaveSettings
        | SettingsSaved
        | SettingsLoaded of SavedSettings
        | Reset

    //TODO see instead https://docs.fabulous.dev/advanced/saving-and-restoring-app-state
    module Settings =
        let private getPath = Path.Combine(Folder.GetPath Folders.cache, "ui-settings.json")
        let requestSave = Cmd.debounce 1000 (fun () -> SaveSettings)

        let load = 
            async {
                let path = getPath

                if File.Exists path then
                    let! json = File.ReadAllTextAsync(getPath) |> Async.AwaitTask
                    let settings = JsonSerializer.Deserialize json
                    return SettingsLoaded settings
                else return Reset
            }
            |> Cmd.ofAsyncMsg

        let save model =
            async {
                let settings = {
                    Top = model.Top
                    CacheHours = model.CacheHours

                    OrderByScore = model.OrderByScore
                    OrderDesc = model.OrderDesc
                    Padding = model.Padding

                    OutputHtml = model.OutputHtml
                    OutputTo = model.OutputTo
                    OpenOutput = model.OpenOutput
                }

                let json = JsonSerializer.Serialize settings
                do! File.WriteAllTextAsync(getPath, json) |> Async.AwaitTask
                return SettingsSaved // Cmd.none?
            }
            |> Cmd.ofAsyncMsg

    let private mapToSearchCommand model =
        let order = 
            match (model.OrderByScore, model.OrderDesc) with
            | (true, true) -> [SearchCommand.OrderOptions.score]
            | (true, false) -> [SearchCommand.OrderOptions.score; SearchCommand.OrderOptions.asc]
            | (false, true) -> [SearchCommand.OrderOptions.uploaded]
            | (false, false) -> [SearchCommand.OrderOptions.uploaded; SearchCommand.OrderOptions.asc]

        let scope =
            match model.Scope with
            | Scopes.videos -> VideosScope(model.Aliases.Split [|' '|]) :> CommandScope
            | Scopes.playlist -> PlaylistScope(model.Aliases, model.Top.Value |> uint16, model.CacheHours.Value |> float32)
            | Scopes.channel -> ChannelScope(model.Aliases, model.Top.Value |> uint16, model.CacheHours.Value |> float32)
            | _ -> failwith ("unknown scope " + model.Scope.ToString())

        let command = SearchCommand()
        command.Query <- model.Query
        command.Padding <- model.Padding |> uint16
        command.OrderBy <- order
        command.Scope <- scope
        command.OutputHtml <- model.OutputHtml
        command.FileOutputPath <- model.OutputTo

        if model.OpenOutput = OpenOutputOptions.file
        then command.Show <- OutputCommand.Shows.file
        elif model.OpenOutput = OpenOutputOptions.folder
        then command.Show <- OutputCommand.Shows.folder

        command

    let private validateChannelScope (scope: CommandScope) youTube dataStore cancellation =
        match scope with
        | :? ChannelScope as channel ->
            async {
                return! CommandValidator.RemoteValidateChannelAsync(channel, youTube, dataStore, cancellation) |> Async.AwaitTask
            }
        | _ ->  async { return () }

    let private searchCmd model =
        fun dispatch ->
            async {
                let command = mapToSearchCommand model
                CommandValidator.ValidateSearchCommand command
                let cacheFolder = Folder.GetPath Folders.cache
                let dataStore = JsonFileDataStore cacheFolder
                let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)
                use cts = new CancellationTokenSource()
                do! validateChannelScope command.Scope youtube.Client dataStore cts.Token

                do! youtube.SearchAsync(command, cts.Token)
                    // see https://github.com/fsprojects/FSharp.Control.TaskSeq
                    |> TaskSeq.iter (fun result -> SearchResult result |> dispatch )
                    |> Async.AwaitTask

                dispatch SearchCompleted
            } |> Async.StartImmediate
        |> Cmd.ofSub

    let private orderResults byScore desc results =
        let sortBy = if desc then List.sortByDescending else List.sortBy

        let comparable: (VideoSearchResult -> IComparable) =
            if byScore then (fun r -> r.Score)
            else (fun r -> r.Video.Uploaded)

        results |> sortBy comparable

    let private saveOutput model =
        async {
            let command = mapToSearchCommand model
            CommandValidator.ValidateSearchCommand command
            let cacheFolder = Folder.GetPath Folders.cache
            let dataStore = JsonFileDataStore cacheFolder
            let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)
            use cts = new CancellationTokenSource()
            do! validateChannelScope command.Scope youtube.Client dataStore cts.Token

            let writer = if model.OutputHtml then new HtmlOutputWriter(command) :> FileOutputWriter else new TextOutputWriter(command)
            writer.WriteHeader()

            for result in orderResults model.OrderDesc model.OrderByScore model.SearchResults do
                writer.WriteVideoResult(result, model.Padding |> uint32)

            // turn ValueTask into Task while native ValueTask handling is in RFC, see https://stackoverflow.com/a/52398452
            let! path = writer.SaveFile().AsTask() |> Async.AwaitTask

            match writer with
            | :? HtmlOutputWriter as htmlWriter -> htmlWriter.Dispose()
            | :? TextOutputWriter as textWriter -> textWriter.Dispose()
            | _ -> failwith "Unknown output writer type."

            // spare the user some file browsing
            if command.Show.HasValue then
                match command.Show.Value with
                | OutputCommand.Shows.file -> ShellCommands.OpenFile path
                | OutputCommand.Shows.folder -> ShellCommands.ExploreFolder path |> ignore
                | _ -> failwith $"Unknown {nameof OutputCommand.Shows} value"

            return SavedOutput path
        }
        |> Cmd.ofAsyncMsg

    //let notificationManager = ViewRef<WindowNotificationManager>()

    let private notify message =
        let notificationManager = FabApplication.Current.WindowNotificationManager
        notificationManager.Show(Notification(message, "", NotificationType.Information))
        Cmd.none

    let initModel = {
        Scope = Scopes.channel
        Aliases = ""
        Query = ""

        Top = Some (float 25)
        OrderByScore = true
        OrderDesc = true
        CacheHours = Some (float 24)

        Padding = 69
        DisplayOutputOptions = false
        OutputHtml = true
        OutputTo = Folder.GetPath Folders.output
        OpenOutput = OpenOutputOptions.nothing

        Searching = false
        SearchResults = []
    }

    // load settings on init, see https://docs.fabulous.dev/basics/application-state/commands#triggering-commands-on-initialization
    let init () = initModel, Settings.load

    let update msg model =
        match msg with
        | ScopeChanged args -> { model with Scope = args.AddedItems.Item 0 :?> Scopes }, Cmd.none
        | AliasesUpdated txt -> { model with Aliases = txt }, Cmd.none
        | QueryChanged txt -> { model with Query = txt }, Cmd.none

        | TopChanged top -> { model with Top = top }, Settings.requestSave()
        | CacheHoursChanged hours -> { model with CacheHours = hours }, Settings.requestSave()

        | OrderByScoreChanged value -> { model with OrderByScore = value }, Settings.requestSave()
        | OrderDescChanged value -> { model with OrderDesc = value }, Settings.requestSave()
        | PaddingChanged padding -> { model with Padding = int padding.Value }, Settings.requestSave()

        | DisplayOutputOptionsChanged output -> { model with DisplayOutputOptions = output }, Cmd.none
        | OutputHtmlChanged value -> { model with OutputHtml = value }, Settings.requestSave()
        | OutputToChanged path -> { model with OutputTo = path }, Settings.requestSave()
        | OpenOutputChanged args -> { model with OpenOutput = args.AddedItems.Item 0 :?> OpenOutputOptions }, Settings.requestSave()
        | SaveOutput -> model, saveOutput model
        | SavedOutput path -> model, notify("Saved results to " + path)

        | Search on -> { model with Searching = on; SearchResults = [] }, (if on then searchCmd model else Cmd.none)
        | SearchResult result -> { model with SearchResults = result::model.SearchResults }, Cmd.none
        | SearchCompleted -> { model with Searching = false }, Cmd.none

        | Notify message -> model, notify message
        | OpenUrl url -> model, (fun _ -> ShellCommands.OpenUri(url); Cmd.none)()
        | CopyingToClipboard _args -> model, Cmd.none
        | SaveSettings -> model, Settings.save model
        | SettingsSaved -> model, Cmd.none
        | SettingsLoaded s -> ({
            model with
                Top = s.Top
                CacheHours = s.CacheHours
                OrderByScore = s.OrderByScore
                OrderDesc = s.OrderDesc
                Padding = s.Padding
                OutputHtml = s.OutputHtml
                OutputTo = s.OutputTo
                OpenOutput = s.OpenOutput
        }, Cmd.none)
        | Reset -> initModel, Cmd.none

    // see https://docs.fabulous.dev/basics/user-interface/styling
    [<Extension>]
    type SharedStyle =

        [<Extension>]
        static member inline trailingMargin(this: WidgetBuilder<'msg, #IFabLayoutable>) = this.margin(0 ,0, 0, 5)

        [<Extension>]
        static member inline demoted(this: WidgetBuilder<'msg, IFabTextBlock>) = this.foreground(Colors.Gray)

    let private displayScope = function
    | Scopes.videos -> "📼 videos"
    | Scopes.playlist -> "▶️ playlist"
    | Scopes.channel -> "📺 channel"
    | _ -> failwith "unknown scope"

    let private displayOpenOutput = function
    | OpenOutputOptions.nothing -> "nothing"
    | OpenOutputOptions.file -> "📄 file"
    | OpenOutputOptions.folder -> "📂 folder"
    | _ -> failwith "unknown Show Option"

    // see https://github.com/AvaloniaUI/Avalonia/discussions/9654
    let private writeHighlightingMatches (matched: MatchedText) (matchPadding: uint32 option) =
        let tb = SelectableTextBlock(CopyingToClipboard)
        let padding = match matchPadding with Some value -> Nullable(value) | None -> Nullable()

        let runs = matched.WriteHighlightingMatches(
            (fun text -> Run(text)),
            (fun text -> Run(text).foreground(Colors.Orange)),
            padding)

        let contents = runs |> Seq.map tb.Yield |> Seq.toList
        let content = Seq.fold (fun agg cont -> tb.Combine(agg, cont)) contents.Head contents.Tail
        (tb.Run content).textWrapping(TextWrapping.WrapWithOverflow)

    let private renderSearchResult (matchPadding: uint32) (result: VideoSearchResult) =
        let videoUrl = Youtube.GetVideoUrl result.Video.Id

        (VStack() {
            Grid(coldefs = [Auto; Auto; Star], rowdefs = [Auto]) {
                (match result.TitleMatches with
                | null -> SelectableTextBlock(result.Video.Title, CopyingToClipboard)
                | matches -> writeHighlightingMatches matches None).fontSize(18)

                Button("↗", OpenUrl videoUrl)
                    .tip(ToolTip("Open video in browser"))
                    .padding(5, 1).margin(5, 0).gridColumn(1)

                TextBlock("📅" + result.Video.Uploaded.ToString())
                    .tip(ToolTip("uploaded"))
                    .textAlignment(TextAlignment.Right).gridColumn(2)
            }

            if result.DescriptionMatches <> null then
                HStack() {
                    (TextBlock "in description").demoted()

                    for matches in result.DescriptionMatches.SplitIntoPaddedGroups(matchPadding) do
                        writeHighlightingMatches matches (Some matchPadding)
                }

            if result.KeywordMatches.HasAny() then
                HStack() {
                    (TextBlock "in keywords").demoted()

                    for matches in result.KeywordMatches do
                        writeHighlightingMatches matches (Some matchPadding)
                }

            if result.MatchingCaptionTracks.HasAny() then
                for trackResult in result.MatchingCaptionTracks do
                    TextBlock(trackResult.Track.LanguageName + " | " + trackResult.Track.FieldName).demoted()
                    let displaysHour = trackResult.HasMatchesWithHours(matchPadding)
                    let splitMatches = trackResult.Matches.SplitIntoPaddedGroups(matchPadding)

                    for matched in splitMatches do
                        let (synced, captionAt) = trackResult.SyncWithCaptions(matched, matchPadding).ToTuple()
                        let offset = TimeSpan.FromSeconds(captionAt).FormatWithOptionalHours().PadLeft(if displaysHour then 7 else 5)

                        Grid(coldefs = [Auto; Auto; Star], rowdefs = [Auto]) {
                            (TextBlock offset).demoted()

                            Button("↗", OpenUrl $"{videoUrl}?t={captionAt}")
                                .tip(ToolTip($"Open video at {offset} in browser"))
                                .padding(5, 1).margin(5, 0).verticalAlignment(VerticalAlignment.Top).gridColumn(1)

                            (writeHighlightingMatches synced (Some matchPadding)).gridColumn(2)
                        }
        }).trailingMargin()

    (*  see for F#
            https://fsharp.org/learn/
            https://github.com/knocte/2fsharp/blob/master/csharp2fsharp.md
            https://github.com/ChrisMarinos/FSharpKoans
        see for app design
            https://github.com/TimLariviere/FabulousContacts/tree/master/FabulousContacts
            https://docs.fabulous.dev/samples-and-tutorials/samples
            https://github.com/jimbobbennett/Awesome-Fabulous
        see for widgets
            https://github.com/fabulous-dev/Fabulous.Avalonia/tree/main/src/Fabulous.Avalonia/Views
            https://play.avaloniaui.net/ *)
    let view model =
        (Grid(coldefs = [Star], rowdefs = [Auto; Auto; Auto; Auto; Star]) {

            // search options
            (Grid(coldefs = [Auto; Star; Auto; Stars 2; Auto], rowdefs = [Auto]) {
                ComboBox(Enum.GetValues<Scopes>(), fun scope -> ComboBoxItem(displayScope scope))
                    .selectedItem(model.Scope).onSelectionChanged(ScopeChanged)
                TextBox(model.Aliases, AliasesUpdated)
                    .watermark("by " + (if model.Scope = Scopes.videos then "space-separated IDs or URLs"
                        elif model.Scope = Scopes.playlist then "ID or URL"
                        else "handle, slug, user name, ID or URL"))
                    .gridColumn(1)
                TextBlock("for")
                    .margin(10, 0).centerVertical().gridColumn(2)
                TextBox(model.Query, QueryChanged)
                    .watermark("your query")
                    .gridColumn(3)
                ToggleButton((if model.Searching then "🛑 Stop" else "🔍 Search"), model.Searching, Search)
                    .margin(10, 0)
                    .gridColumn(4)
            }).trailingMargin()

            // playlist options
            (Grid(coldefs = [Auto; Star; Star], rowdefs = [Auto]) {
                Label "in playlists and channels"

                (HStack(5) {
                    Label "search top"
                    NumericUpDown(0, float UInt16.MaxValue, model.Top, TopChanged)
                        .formatString("F0")
                        .tip(ToolTip("number of videos to search"))
                    Label "videos"
                }).gridColumn(1).centerHorizontal()

                (HStack(5) {
                    Label "and look for new ones after"
                    NumericUpDown(0, float UInt16.MaxValue, model.CacheHours, CacheHoursChanged)
                        .formatString("F0")
                        .tip(ToolTip("The info about which videos are in a playlist or channel is cached locally to speed up future searches."
                            + " This controls after how many hours such a cache is considered stale."
                            + Environment.NewLine + Environment.NewLine
                            + "Note that this doesn't concern the video data caches,"
                            + " which are not expected to change often and are stored until you explicitly clear them."))
                    Label "hours"
                }).gridColumn(2).centerHorizontal()
            }).gridRow(1).trailingMargin()

            // result options
            (Grid(coldefs = [Auto; Star; Star; Auto], rowdefs = [Auto]) {
                TextBlock("Results")

                (HStack(5) {
                    let direction =
                        match (model.OrderByScore, model.OrderDesc) with
                        | (true ,true) -> "⋱ highest"
                        | (true ,false) -> "⋰ lowest"
                        | (false, true) -> "⋱ latest"
                        | (false, false) -> "⋰ earliest"

                    Label "ordered by"
                    ToggleButton(direction, model.OrderDesc, OrderDescChanged)
                    ToggleButton((if model.OrderByScore then "💯 score" else "📅 uploaded"), model.OrderByScore, OrderByScoreChanged)
                    Label "first"
                }).gridColumn(1).centerVertical().centerHorizontal()

                (HStack(5) {
                    Label "padded with"
                    NumericUpDown(0, float UInt16.MaxValue, Some (float model.Padding), PaddingChanged)
                        .increment(5).formatString("F0")
                        .tip(ToolTip("how much context to show a search result in"))
                    Label "chars for context"
                }).gridColumn(2).centerHorizontal()

                ToggleButton("to file 📄", model.DisplayOutputOptions, DisplayOutputOptionsChanged).gridColumn(3)
            }).gridRow(2).trailingMargin()

            // output options
            (Grid(coldefs = [Auto; Auto; Auto; Star; Auto; Auto; Auto], rowdefs = [Auto]) {
                Label("ouput")
                ToggleButton((if model.OutputHtml then "🖺 html" else "🖹 text"), model.OutputHtml, OutputHtmlChanged).gridColumn(1)
                Label("to").gridColumn(2)
                TextBox(model.OutputTo, OutputToChanged)
                    .watermark("where to save the output file").gridColumn(3)
                Label("and open").gridColumn(4)
                ComboBox(Enum.GetValues<OpenOutputOptions>(), fun show -> ComboBoxItem(displayOpenOutput show))
                    .selectedItem(model.OpenOutput).onSelectionChanged(OpenOutputChanged).gridColumn(5)
                Button("💾 Save", SaveOutput).gridColumn(6)
            }).isVisible(model.DisplayOutputOptions).gridRow(3).trailingMargin()

            // results
            ScrollViewer((VStack() {
                for result in orderResults model.OrderDesc model.OrderByScore model.SearchResults do
                    renderSearchResult (model.Padding |> uint32) result
            })).gridRow(4)

            (*View.WindowNotificationManager(notificationManager)
                .position(NotificationPosition.BottomRight)
                .maxItems(3)*)
        }).margin(5, 5 , 5, 0)

#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model = DesktopApplication(Window(view model))
#endif

    let theme = FluentTheme()
    let program = Program.statefulWithCmd init update app
