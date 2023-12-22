namespace Ui

open System
open Avalonia.Controls
open Avalonia.Themes.Fluent
open Fabulous
open Fabulous.Avalonia
open SubTubular

open type Fabulous.Avalonia.View

module App =
    type Scopes = videos = 0 | playlist = 1 | channel = 2
    type ShowOptions = nothing = 0 | file = 1 | folder = 2
    //type OutputOptions = html = 0 | text = 1
    //type OrderDirections = asc = 0 | desc = 1

    type Model = {
        Scope: Scopes
        Aliases: string
        Query: string
        Searching: bool
        Padding: int
        Top: float option
        OrderByScore: bool
        OrderDesc: bool
        CacheHours: float option

        Output: bool
        OutputHtml: bool
        OutputTo: string
        ShowOutput: ShowOptions
    }

    type Msg =
        | ScopeChanged of SelectionChangedEventArgs
        | AliasesUpdated of string
        | QueryChanged of string

        | TopChanged of float option
        | OrderByScoreChanged of bool
        | OrderDescChanged of bool
        | CacheHoursChanged of float option

        | PaddingChanged of float option
        | OutputChanged of bool
        | OutputHtmlChanged of bool
        | OutputToChanged of string
        | ShowOutputChanged of SelectionChangedEventArgs

        | Search of bool
        | SearchCompleted
        | Reset

    let searchCmd model =
        async {
            let cacheFolder = Folder.GetPath Folders.cache
            let dataStore = JsonFileDataStore cacheFolder
            let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)
            //Func<IAsyncEnumerable<VideoSearchResult>> getResultsAsync;
            do! Async.Sleep 500

            return SearchCompleted
        }
        |> Cmd.ofAsyncMsg

    let initModel = {
        Scope = Scopes.channel
        Aliases = ""
        Query = ""

        Top = Some (float 50)
        OrderByScore = true
        OrderDesc = true
        CacheHours = Some (float 24)

        Padding = 23
        Output = false
        OutputHtml = true
        OutputTo = Folder.GetPath Folders.output
        ShowOutput = ShowOptions.nothing

        Searching = false
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
        | OutputChanged output -> { model with Output = output }, Cmd.none
        | OutputHtmlChanged value -> { model with OutputHtml = value }, Cmd.none
        | OutputToChanged path -> { model with OutputTo = path }, Cmd.none
        | ShowOutputChanged args ->
            { model with ShowOutput = args.AddedItems.Item 0 :?> ShowOptions },
            Cmd.none

        | Search on -> { model with Searching = on }, (if on then searchCmd model else Cmd.none)
        | SearchCompleted -> { model with Searching = false }, Cmd.none
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
        Grid(coldefs = [Star], rowdefs = [Auto; Auto; Star; Auto]) {

            Grid(coldefs = [Auto; Star; Auto; Star; Auto], rowdefs = [Auto]) {
                (ComboBox(Enum.GetValues<Scopes>(), fun scope -> ComboBoxItem(scope.ToString())))
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
                ToggleButton((if model.Searching then "Stop" else "Search"), model.Searching, Search)
                    .margin(10, 0)
                    .gridColumn(4)
            }

            (Grid(coldefs = [Star], rowdefs = [Auto]) {
                (HStack(5) {
                    Label "search top"
                    NumericUpDown(0, float UInt16.MaxValue, model.Top, TopChanged)
                        .tooltip("number of videos to search")
                    Label "videos in playlists"
                    Label "and refresh after"
                    NumericUpDown(0, float UInt16.MaxValue, model.CacheHours, CacheHoursChanged)
                        .tooltip("number of hours to cache playlist videos")
                    Label "hours"
                }).gridColumn(0)
            }).gridRow(1)

            (Grid(coldefs = [Auto; Star; Auto], rowdefs = [Auto]) {
                Label("Results").gridColumn(0)

                (HStack(5) {
                    Label "ordered"
                    ToggleButton((if model.OrderDesc then "desc" else "asc"), model.OrderDesc, OrderDescChanged)
                    //ToggleSplitButton((if model.OrderByScore then "score" else "uploaded"), model.OrderByScore, OrderByScoreChanged)
                    Label "by"
                    ToggleButton((if model.OrderByScore then "score" else "uploaded"), model.OrderByScore, OrderByScoreChanged)
                    (*RadioButton("score", model.OrderByScore, OrderByScoreChanged).groupName(nameof(model.OrderByScore))
                    RadioButton("uploaded", not model.OrderByScore, not >> OrderByScoreChanged).groupName(nameof(model.OrderByScore))
                    RadioButton("asc", not model.OrderDesc, not >> OrderDescChanged).groupName(nameof(model.OrderDesc))
                    RadioButton("desc", model.OrderDesc, OrderDescChanged).groupName(nameof(model.OrderDesc))*)
                }).gridColumn(1).centerVertical()

                (HStack(5) {
                    Label "padded with"
                    NumericUpDown(0, float UInt16.MaxValue, Some (float model.Padding), PaddingChanged)
                        .tooltip("how much context to show a search result in")
                    Label "chars"
                }).gridColumn(2)
            }).gridRow(2)

            // output
            (Grid(coldefs = [Auto; Auto; Auto; Star; Auto; Auto], rowdefs = [Auto]) {
                CheckBox("ouput", model.Output, OutputChanged).gridColumn(0)
                ToggleButton((if model.OutputHtml then "html" else "text"), model.OutputHtml, OutputHtmlChanged).gridColumn(1)
                Label("to").gridColumn(2)
                TextBox(model.OutputTo, OutputToChanged)
                    .watermark("where to save the output file").gridColumn(3)
                Label("and show").gridColumn(4)
                ComboBox(Enum.GetValues<ShowOptions>(), fun show -> ComboBoxItem(show.ToString()))
                    .selectedItem(model.ShowOutput).onSelectionChanged(ShowOutputChanged).gridColumn(5)
            }).gridRow(3)
        }

    
#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model = DesktopApplication(Window(view model))
#endif

    
    let theme = FluentTheme()

    let program = Program.statefulWithCmd init update app
