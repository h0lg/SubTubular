namespace Ui

open System
open Avalonia.Controls
open Avalonia.Themes.Fluent
open Fabulous
open Fabulous.Avalonia
open type Fabulous.Avalonia.View
open SubTubular
open SubTubular.Extensions

open FSharp.Control
open System.Threading
open System.Runtime.CompilerServices
open Avalonia.Media

module App =
    type Scopes = videos = 0 | playlist = 1 | channel = 2
    type OpenOutputOptions = nothing = 0 | file = 1 | folder = 2
    //type OutputOptions = html = 0 | text = 1
    //type OrderDirections = asc = 0 | desc = 1

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

        | OutputChanged of bool
        | OutputHtmlChanged of bool
        | OutputToChanged of string
        | OpenOutputChanged of SelectionChangedEventArgs

        | Search of bool
        | SearchResult of VideoSearchResult
        | SearchCompleted

        | OpenUrl of string
        | Reset

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

    let private searchCmd model =
        fun dispatch ->
            async {
                let cacheFolder = Folder.GetPath Folders.cache
                let dataStore = JsonFileDataStore cacheFolder
                let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)

                let order = 
                    match (model.OrderByScore, model.OrderDesc) with
                    | (true, true) -> [PlaylistLikeScope.OrderOptions.score]
                    | (true, false) -> [PlaylistLikeScope.OrderOptions.score; PlaylistLikeScope.OrderOptions.asc]
                    | (false, true) -> [PlaylistLikeScope.OrderOptions.uploaded]
                    | (false, false) -> [PlaylistLikeScope.OrderOptions.uploaded; PlaylistLikeScope.OrderOptions.asc]

                let scope =
                    match model.Scope with
                    | Scopes.videos -> VideosScope(model.Aliases.Split [|' '|]) :> CommandScope
                    | Scopes.playlist -> PlaylistScope(model.Aliases, model.Top.Value |> uint16, order, model.CacheHours.Value |> float32)
                    | Scopes.channel -> ChannelScope(model.Aliases, model.Top.Value |> uint16, order, model.CacheHours.Value |> float32)
                    | _ -> failwith ("unknown scope " + model.Scope.ToString())

                let command = SearchCommand()
                command.Query <- model.Query
                command.Padding <- model.Padding |> uint16
                command.Scope <- scope
                command.OutputHtml <- model.OutputHtml
                command.FileOutputPath <- model.OutputTo

                if model.OpenOutput = OpenOutputOptions.file
                then command.Show <- OutputCommand.Shows.file
                elif model.OpenOutput = OpenOutputOptions.folder
                then command.Show <- OutputCommand.Shows.folder

                CommandValidator.ValidateSearchCommand command
                use cts = new CancellationTokenSource()

                do!
                    match command.Scope with
                    | :? ChannelScope as channel ->
                        async {
                            return! CommandValidator.RemoteValidateChannelAsync(channel, youtube.Client, dataStore, cts.Token) |> Async.AwaitTask
                        }
                    | _ ->  async { return () }

                do! youtube.SearchAsync(command, cts.Token) |> TaskSeq.iter (fun result -> SearchResult result |> dispatch ) |> Async.AwaitTask
                dispatch SearchCompleted
            } |> Async.StartImmediate
        |> Cmd.ofSub

    let initModel = {
        Scope = Scopes.channel
        Aliases = ""
        Query = ""

        Top = Some (float 5)
        OrderByScore = true
        OrderDesc = true
        CacheHours = Some (float 24)

        Padding = 23
        DisplayOutputOptions = false
        OutputHtml = true
        OutputTo = Folder.GetPath Folders.output
        OpenOutput = OpenOutputOptions.nothing

        Searching = false
        SearchResults = []
    }

    let init () = initModel, Cmd.none

    let update msg model =
        match msg with
        | ScopeChanged args -> { model with Scope = args.AddedItems.Item 0 :?> Scopes }, Cmd.none
        | AliasesUpdated txt -> { model with Aliases = txt }, Cmd.none
        | QueryChanged txt -> { model with Query = txt }, Cmd.none

        | TopChanged top -> { model with Top = top }, Cmd.none
        | OrderByScoreChanged value -> { model with OrderByScore = value }, Cmd.none
        | OrderDescChanged value -> { model with OrderDesc = value }, Cmd.none
        | CacheHoursChanged hours -> { model with CacheHours = hours }, Cmd.none

        | PaddingChanged padding -> { model with Padding = int padding.Value }, Cmd.none
        | OutputChanged output -> { model with DisplayOutputOptions = output }, Cmd.none
        | OutputHtmlChanged value -> { model with OutputHtml = value }, Cmd.none
        | OutputToChanged path -> { model with OutputTo = path }, Cmd.none
        | OpenOutputChanged args -> { model with OpenOutput = args.AddedItems.Item 0 :?> OpenOutputOptions }, Cmd.none

        | Search on -> { model with Searching = on; SearchResults = [] }, (if on then searchCmd model else Cmd.none)
        | SearchResult result -> { model with SearchResults = result::model.SearchResults }, Cmd.none
        | SearchCompleted -> { model with Searching = false }, Cmd.none

        | OpenUrl url -> model, (fun _ -> ShellCommands.OpenUri(url); Cmd.none)()
        | Reset -> initModel, Cmd.none

    (*TODO look at ToggleSplitButton implementation
    let internal labeledInput (label: string) ([<ParamArray>] inputs : WidgetBuilder<_,IFabTemplatedControl> array) =
        //let children =  :: inputs

        let stack = HStack() {
            Label(label) |> ignore
            for input in inputs -> input
        }

        //for input in inputs do
            //stack.

        stack*)

        // see https://docs.fabulous.dev/basics/user-interface/styling
    [<Extension>]
    type SharedStyle =
        static write = fun text -> Run(text)
        static highlight = fun text -> Run(text).foreground(Colors.Orange)

        [<Extension>]
        static member inline marked(this: WidgetBuilder<'msg, #IFabRun>) =
            this.foreground(Colors.Blue) |> ignore
            this

        (*[<Extension>]
        static member inline inlines(this: WidgetBuilder<'msg, #IFabTextBlock>, value: IEnumerable<#IFabInline>) =
            this.AddWidgetCollection(TextBlock.Inlines.WithValue(value))*)

    // see https://github.com/AvaloniaUI/Avalonia/discussions/9654
    //View.map ?
    let private writeHighlightingMatches (matched: MatchedText) (matchPadding: uint32 option) =
        let tb = TextBlock()
        let padding = match matchPadding with Some value -> Nullable(value) | None -> Nullable()
        let runs = matched.WriteHighlightingMatches(SharedStyle.write, SharedStyle.highlight, padding)
        let contents = runs |> Seq.map tb.Yield |> Seq.toList
        let content = Seq.fold (fun agg cont -> tb.Combine(agg, cont)) contents.Head contents.Tail
        tb.Run content

    let private renderSearchResult (matchPadding: uint32) (result: VideoSearchResult) =
        let videoUrl = Youtube.GetVideoUrl result.Video.Id

        VStack() {
            Grid(coldefs = [Auto; Auto; Star], rowdefs = [Auto]){
                (match result.TitleMatches with
                | null -> TextBlock result.Video.Title
                | matches -> writeHighlightingMatches matches None).gridColumn(0)

                Button("↗", OpenUrl videoUrl)
                    .tip(ToolTip("Open video in browser"))
                    .padding(5, 1).margin(5, 0).gridColumn(1)

                TextBlock("📅" + result.Video.Uploaded.ToString())
                    .tip(ToolTip("uploaded"))
                    .textAlignment(TextAlignment.Right).gridColumn(2)
            }

            if result.DescriptionMatches <> null then
                for matches in result.DescriptionMatches.SplitIntoPaddedGroups(matchPadding) do
                    writeHighlightingMatches matches (Some matchPadding)

            if result.KeywordMatches.HasAny() then
                HStack() {
                    for matches in result.KeywordMatches do
                        writeHighlightingMatches matches (Some matchPadding)
                }

            if result.MatchingCaptionTracks.HasAny() then
                for trackResult in result.MatchingCaptionTracks do
                    TextBlock (trackResult.Track.LanguageName + " | " + trackResult.Track.FieldName)
                    let displaysHour = trackResult.HasMatchesWithHours(matchPadding)
                    let splitMatches = trackResult.Matches.SplitIntoPaddedGroups(matchPadding)

                    for matched in splitMatches do
                        let captionAt = trackResult.SyncWithCaptions(matched, matchPadding)
                        let offset = TimeSpan.FromSeconds(captionAt).FormatWithOptionalHours().PadLeft(if displaysHour then 7 else 5)

                        HStack() {
                            TextBlock offset
                            writeHighlightingMatches matched (Some matchPadding)

                            Button("↗", OpenUrl $"{videoUrl}?t={captionAt}")
                                .tip(ToolTip($"Open video at {offset} in browser"))
                                .padding(5, 1).margin(5, 0)
                        }
        }

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
        Grid(coldefs = [Star], rowdefs = [Auto; Auto; Auto; Auto; Star]) {

            // search options
            Grid(coldefs = [Auto; Star; Auto; Stars 2; Auto], rowdefs = [Auto]) {
                ComboBox(Enum.GetValues<Scopes>(), fun scope -> ComboBoxItem(displayScope scope))
                    .selectedItem(model.Scope).onSelectionChanged(ScopeChanged).gridColumn(0)
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
            }

            // playlist options
            (Grid(coldefs = [Star], rowdefs = [Auto]) {
                (HStack(5) {
                    Label "in playlists and channels"
                    Label "search top"
                    NumericUpDown(0, float UInt16.MaxValue, model.Top, TopChanged)
                        .formatString("F0")
                        .tip(ToolTip("number of videos to search"))
                    Label "videos"
                    Label "and look for new ones after"
                    NumericUpDown(0, float UInt16.MaxValue, model.CacheHours, CacheHoursChanged)
                        .formatString("F0")
                        .tip(ToolTip("The info about which videos are in a playlist or channel is cached locally to speed up future searches."
                            + " This controls after how many hours such a cache is considered stale."
                            + Environment.NewLine + Environment.NewLine
                            + "Note that this doesn't concern the video data caches,"
                            + " which are not expected to change often and are stored until you explicitly clear them."))
                    Label "hours"
                }).gridColumn(0)
            }).gridRow(1)

            // result options
            (Grid(coldefs = [Auto; Star; Auto; Auto], rowdefs = [Auto]) {
                TextBlock("Results").gridColumn(0)

                (HStack(5) {
                    Label "ordered"
                    ToggleButton((if model.OrderDesc then "descending ↓" else "ascending ↑"), model.OrderDesc, OrderDescChanged)
                    Label "by"
                    ToggleButton((if model.OrderByScore then "💯 score" else "📅 uploaded"), model.OrderByScore, OrderByScoreChanged)
                }).gridColumn(1).centerVertical()

                (HStack(5) {
                    Label "padded with"
                    NumericUpDown(0, float UInt16.MaxValue, Some (float model.Padding), PaddingChanged)
                        .formatString("F0")
                        .tip(ToolTip("how much context to show a search result in"))
                    Label "chars for context"
                }).gridColumn(2)

                ToggleButton("📄 output", model.DisplayOutputOptions, OutputChanged).gridColumn(3)
            }).gridRow(2)

            // output options
            (Grid(coldefs = [Auto; Auto; Auto; Star; Auto; Auto], rowdefs = [Auto]) {
                Label("ouput").gridColumn(0)
                //CheckBox("ouput", model.Output, OutputChanged).gridColumn(0)
                ToggleButton((if model.OutputHtml then "🖺 html" else "🖹 text"), model.OutputHtml, OutputHtmlChanged).gridColumn(1)
                Label("to").gridColumn(2)
                TextBox(model.OutputTo, OutputToChanged)
                    .watermark("where to save the output file").gridColumn(3)
                Label("and open").gridColumn(4)
                ComboBox(Enum.GetValues<OpenOutputOptions>(), fun show -> ComboBoxItem(displayOpenOutput show))
                    .selectedItem(model.OpenOutput).onSelectionChanged(OpenOutputChanged).gridColumn(5)
            }).gridRow(3).isVisible(model.DisplayOutputOptions)

            View.ListBox(model.SearchResults, renderSearchResult (model.Padding |> uint32)).gridRow(4)
        }

#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model = DesktopApplication(Window(view model))
#endif

    let theme = FluentTheme()
    let program = Program.statefulWithCmd init update app
