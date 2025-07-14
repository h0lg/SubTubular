﻿namespace SubTubular.Gui

open System
open System.IO
open System.Runtime.CompilerServices
open System.Text.Json
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Themes.Fluent
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module App =
    type Scopes = videos = 0 | playlist = 1 | channel = 2
    type OpenOutputOptions = nothing = 0 | file = 1 | folder = 2

    type SavedSettings = {
        OrderByScore: bool
        OrderDesc: bool
        Padding: int

        OutputHtml: bool
        OutputTo: string
        OpenOutput: OpenOutputOptions
    }

    type Scope = {
        Type: Scopes
        Aliases: string

        DisplaysSettings: bool
        Top: float option
        CacheHours: float option
        Progress: BatchProgress.VideoList option
    }

    type Model = {
        Notifier: WindowNotificationManager

        Scopes: Scope list
        Query: string

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
        | QueryChanged of string

        | AddScope of Scopes
        | RemoveScope of Scope
        | AliasesUpdated of Scope * string
        | DisplaySettingsChanged of Scope * bool
        | TopChanged of Scope * float option
        | CacheHoursChanged of Scope * float option

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
        | SearchProgress of BatchProgress
        | ProgressChanged of float
        | SearchCompleted

        | AttachedToVisualTreeChanged of VisualTreeAttachmentEventArgs
        | Notify of string
        | CopyingToClipboard of RoutedEventArgs
        | OpenUrl of string
        | SaveSettings
        | SettingsSaved
        | SettingsLoaded of SavedSettings
        | Reset

    //TODO see instead https://docs.fabulous.dev/advanced/saving-and-restoring-app-state
    module Settings =
        let private path = Path.Combine(Folder.GetPath Folders.storage, "ui-settings.json")
        let requestSave = Cmd.debounce 1000 (fun () -> SaveSettings)

        let load = 
            async {
                if File.Exists path then
                    let! json = File.ReadAllTextAsync(path) |> Async.AwaitTask
                    let settings = JsonSerializer.Deserialize json
                    return SettingsLoaded settings
                else return Reset
            }
            |> Cmd.OfAsync.msg

        let save model =
            async {
                let settings = {
                    OrderByScore = model.OrderByScore
                    OrderDesc = model.OrderDesc
                    Padding = model.Padding

                    OutputHtml = model.OutputHtml
                    OutputTo = model.OutputTo
                    OpenOutput = model.OpenOutput
                }

                let json = JsonSerializer.Serialize settings
                do! File.WriteAllTextAsync(path, json) |> Async.AwaitTask
                return SettingsSaved // Cmd.none?
            }
            |> Cmd.OfAsync.msg

    let private mapToSearchCommand model =
        let order = 
            match (model.OrderByScore, model.OrderDesc) with
            | (true, true) -> [SearchCommand.OrderOptions.score]
            | (true, false) -> [SearchCommand.OrderOptions.score; SearchCommand.OrderOptions.asc]
            | (false, true) -> [SearchCommand.OrderOptions.uploaded]
            | (false, false) -> [SearchCommand.OrderOptions.uploaded; SearchCommand.OrderOptions.asc]

        let command = SearchCommand()
        let getScopes scope = model.Scopes |> List.filter (fun s -> s.Type = scope && s.Aliases.IsNonWhiteSpace())

        command.Channels <- getScopes Scopes.channel
            |> List.map(fun scope -> ChannelScope(scope.Aliases, scope.Top.Value |> uint16, scope.CacheHours.Value |> float32))
            |> List.toArray

        command.Playlists <- getScopes Scopes.playlist
            |> List.map(fun scope -> PlaylistScope(scope.Aliases, scope.Top.Value |> uint16, scope.CacheHours.Value |> float32))
            |> List.toArray

        let videos = getScopes Scopes.videos |> List.tryExactlyOne
        if videos.IsSome then command.Videos <- VideosScope(videos.Value.Aliases.Split [|' '|])

        command.Query <- model.Query
        command.Padding <- model.Padding |> uint16
        command.OrderBy <- order
        command.OutputHtml <- model.OutputHtml
        command.FileOutputPath <- model.OutputTo

        if model.OpenOutput = OpenOutputOptions.file
        then command.Show <- OutputCommand.Shows.file
        elif model.OpenOutput = OpenOutputOptions.folder
        then command.Show <- OutputCommand.Shows.folder

        command

    let private searchCmd model =
        fun dispatch ->
            async {
                let command = mapToSearchCommand model
                let cacheFolder = Folder.GetPath Folders.cache
                let dataStore = JsonFileDataStore cacheFolder
                let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)
                let dispatchProgress = CmdExtensions.bufferedThrottle 100 (fun progress -> SearchProgress progress)
                command.SetProgressReporter(Progress<BatchProgress>(fun progress -> dispatchProgress progress |> List.iter (fun effect -> effect dispatch)))
                CommandValidator.PrevalidateSearchCommand command
                use cts = new CancellationTokenSource()
                do! CommandValidator.ValidateScopesAsync(command, youtube, dataStore, cts.Token) |> Async.AwaitTask

                do! youtube.SearchAsync(command, cts.Token)
                    // see https://github.com/fsprojects/FSharp.Control.TaskSeq
                    |> TaskSeq.iter (fun result -> SearchResult result |> dispatch)
                    |> Async.AwaitTask

                dispatch SearchCompleted
            } |> Async.StartImmediate
        |> Cmd.ofEffect

    let private orderResults byScore desc results =
        let sortBy = if desc then List.sortByDescending else List.sortBy

        let comparable: (VideoSearchResult -> IComparable) =
            if byScore then (fun r -> r.Score)
            else (fun r -> r.Video.Uploaded)

        results |> sortBy comparable

    let private saveOutput model =
        async {
            let command = mapToSearchCommand model
            CommandValidator.PrevalidateSearchCommand command
            let cacheFolder = Folder.GetPath Folders.cache
            let dataStore = JsonFileDataStore cacheFolder
            let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)
            use cts = new CancellationTokenSource()
            do! CommandValidator.ValidateScopesAsync(command, youtube, dataStore, cts.Token) |> Async.AwaitTask

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
        |> Cmd.OfAsync.msg

    let private dispatchToUiThread (action: unit -> unit) =
        // Check if the current thread is the UI thread
        if Avalonia.Threading.Dispatcher.UIThread.CheckAccess() then
            action () // run action on current thread
        else
            // If not on the UI thread, invoke the code on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.Invoke(action)

    let private notifyInfo (notifier: WindowNotificationManager) title =
        dispatchToUiThread (fun () ->
            notifier.Show(Notification(title, "", NotificationType.Information, TimeSpan.FromSeconds 3)))

        Cmd.none

    let private createScope scope aliases =
        let isVideos = scope = Scopes.videos
        let top = if isVideos then None else Some (float 50)
        let cacheHours = if isVideos then None else Some (float 24)
        { Type = scope; Aliases = aliases; Top = top; CacheHours = cacheHours; DisplaysSettings = false; Progress = None }

    let initModel = {
        Notifier = null
        Query = ""

        Scopes = [
            createScope Scopes.channel ""
        ]

        OrderByScore = true
        OrderDesc = true

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
        | QueryChanged txt -> { model with Query = txt }, Cmd.none
        | AddScope scope -> { model with Scopes = model.Scopes@[createScope scope ""] }, Cmd.none
        | RemoveScope scope -> { model with Scopes = model.Scopes |> List.except [scope] }, Cmd.none

        | DisplaySettingsChanged (scope, display) ->
            let scopes = model.Scopes |> List.map(fun s -> if s = scope then { s with DisplaysSettings = display } else s)
            { model with Scopes = scopes }, Cmd.none

        | AliasesUpdated (scope, aliases) ->
            let scopes = model.Scopes |> List.map(fun s -> if s = scope then { s with Aliases = aliases } else s)
            { model with Scopes = scopes }, Cmd.none

        | TopChanged (scope, top) ->
            let scopes = model.Scopes |> List.map(fun s -> if s = scope then { s with Top = top } else s)
            { model with Scopes = scopes }, Cmd.none

        | CacheHoursChanged (scope, hours) ->
            let scopes = model.Scopes |> List.map(fun s -> if s = scope then { s with CacheHours = hours } else s)
            { model with Scopes = scopes }, Cmd.none

        | OrderByScoreChanged value -> { model with OrderByScore = value }, Settings.requestSave()
        | OrderDescChanged value -> { model with OrderDesc = value }, Settings.requestSave()
        | PaddingChanged padding -> { model with Padding = int padding.Value }, Settings.requestSave()

        | DisplayOutputOptionsChanged output -> { model with DisplayOutputOptions = output }, Cmd.none
        | OutputHtmlChanged value -> { model with OutputHtml = value }, Settings.requestSave()
        | OutputToChanged path -> { model with OutputTo = path }, Settings.requestSave()
        | OpenOutputChanged args -> { model with OpenOutput = args.AddedItems.Item 0 :?> OpenOutputOptions }, Settings.requestSave()
        | SaveOutput -> model, saveOutput model
        | SavedOutput path -> model, notifyInfo model.Notifier ("Saved results to " + path)

        | Search on -> { model with Searching = on; SearchResults = [] }, (if on then searchCmd model else Cmd.none)
        | SearchResult result -> { model with SearchResults = result::model.SearchResults }, Cmd.none

        | SearchProgress progress ->
            let scopes = model.Scopes |> List.map(fun s ->
                let equals scope (commandScope: CommandScope) =
                    match commandScope with
                    | :? ChannelScope as channel -> channel.Alias = scope.Aliases
                    | :? PlaylistScope as playlist -> playlist.Alias = scope.Aliases
                    | :? VideosScope as videos -> videos.Videos = scope.Aliases.Split [|' '|]
                    | _ -> failwith "quark"

                let scopeProgress = progress.VideoLists |> Seq.tryFind (fun pair -> equals s pair.Key) |> Option.map (fun pair -> pair.Value)
                if scopeProgress.IsSome
                then { s with Progress = scopeProgress }
                else s )

            { model with Scopes = scopes }, Cmd.none

        | ProgressChanged _value -> model, Cmd.none
        | SearchCompleted -> { model with Searching = false }, notifyInfo model.Notifier "search completed"

        | AttachedToVisualTreeChanged args -> 
            let notifier = FabApplication.Current.WindowNotificationManager
            notifier.Position <- NotificationPosition.BottomRight
            { model with Notifier = notifier }, Cmd.none

        | Notify title -> model, notifyInfo model.Notifier title
        | OpenUrl url -> model, (fun _ -> ShellCommands.OpenUri(url); Cmd.none)()
        | CopyingToClipboard _args -> model, Cmd.none
        | SaveSettings -> model, Settings.save model
        | SettingsSaved -> model, Cmd.none

        | SettingsLoaded s -> ({
            model with
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
                    TextBlock(trackResult.Track.LanguageName).demoted()
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

    let view model =
        (Grid(coldefs = [Star], rowdefs = [Auto; Auto; Auto; Auto; Star]) {

            // see https://usecasepod.github.io/posts/mvu-composition.html
            // and https://github.com/TimLariviere/FabulousContacts/blob/0d5024c4bfc7a84f02c0788a03f63ff946084c0b/FabulousContacts/ContactsListPage.fs#L89C17-L89C31
            // search options
            (Grid(coldefs = [Auto; Star; Auto; Stars 2; Auto], rowdefs = [Auto]) {
                TextBlock("for")
                    .margin(10, 0).centerVertical().gridColumn(2)
                TextBox(model.Query, QueryChanged)
                    .watermark("your query")
                    .gridColumn(3)
                ToggleButton((if model.Searching then "🛑 Stop" else "🔍 Search"), model.Searching, Search)
                    .margin(10, 0)
                    .gridColumn(4)
            }).trailingMargin()

            // scopes
            ScrollViewer((VStack() {
                HWrap(){
                    Label "in"

                    for scope in model.Scopes do
                        VStack(5){
                            HStack(5){
                                Label(displayScope scope.Type)

                                TextBox(scope.Aliases, fun value -> AliasesUpdated(scope, value))
                                    .watermark("by " + (if scope.Type = Scopes.videos then "space-separated IDs or URLs"
                                        elif scope.Type = Scopes.playlist then "ID or URL"
                                        else "handle, slug, user name, ID or URL"))

                                (HStack(5) {
                                    Label "search top"
                                    NumericUpDown(0, float UInt16.MaxValue, scope.Top, fun value -> TopChanged(scope, value))
                                        .formatString("F0")
                                        .tip(ToolTip("number of videos to search"))
                                    Label "videos"
                                }).centerHorizontal().isVisible(scope.DisplaysSettings)

                                (HStack(5) {
                                    Label "and look for new ones after"
                                    NumericUpDown(0, float UInt16.MaxValue, scope.CacheHours, fun value -> CacheHoursChanged(scope, value))
                                        .formatString("F0")
                                        .tip(ToolTip("The info about which videos are in a playlist or channel is cached locally to speed up future searches."
                                            + " This controls after how many hours such a cache is considered stale."
                                            + Environment.NewLine + Environment.NewLine
                                            + "Note that this doesn't concern the video data caches,"
                                            + " which are not expected to change often and are stored until you explicitly clear them."))
                                    Label "hours"
                                }).centerHorizontal().isVisible(scope.DisplaysSettings)

                                ToggleButton("⚙", scope.DisplaysSettings, fun display -> DisplaySettingsChanged(scope, display))
                                    .tip(ToolTip("display settings"))

                                Button("❌", RemoveScope scope).tip(ToolTip("remove this scope"))
                            }

                            if scope.Progress.IsSome then
                                ProgressBar(0, scope.Progress.Value.AllJobs, scope.Progress.Value.CompletedJobs, ProgressChanged)
                                    .progressTextFormat(scope.Progress.Value.ToString()).showProgressText(true)
                        }
                }

                HStack(5){
                    Label "add"

                    let hasVideosScope = model.Scopes |> List.exists(fun scope -> scope.Type = Scopes.videos)
                    let allScopes = Enum.GetValues<Scopes>()
                    let addable = if hasVideosScope then allScopes |> Array.except [Scopes.videos] else allScopes

                    for scope in addable do
                        Button(displayScope scope, AddScope scope)
                }
            })).gridRow(1).trailingMargin()

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
            }).gridRow(2).trailingMargin().isVisible(not model.SearchResults.IsEmpty)

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
        }).margin(5, 5 , 5, 0).onAttachedToVisualTree(AttachedToVisualTreeChanged)

#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model = DesktopApplication(Window(view model))
#endif

    let create () =
        let theme () = FluentTheme()

        let program =
            Program.statefulWithCmd init update
            |> Program.withTrace(fun (format, args) -> System.Diagnostics.Debug.WriteLine(format, box args))
            |> Program.withExceptionHandler(fun ex ->
#if DEBUG
                printfn $"Exception: %s{ex.ToString()}"
                false
#else
                true
#endif
            )
            |> Program.withView app

        FabulousAppBuilder.Configure(theme, program)
