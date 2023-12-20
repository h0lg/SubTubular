namespace Ui

open System
open Avalonia.Controls
open Avalonia.Themes.Fluent
open Fabulous
open Fabulous.Avalonia
open SubTubular

open type Fabulous.Avalonia.View

module App =
    type Scopes =
        | videos = 0
        | playlist = 1
        | channel = 2

    type Model = {
        Scope: Scopes
        Aliases: string
        Query: string
        Searching: bool
    }

    type Msg =
        | ScopeChanged of SelectionChangedEventArgs
        | AliasesUpdated of string
        | QueryChanged of string
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
        Searching = false
    }

    let init () = initModel, Cmd.none

    let update msg model =
        match msg with
        | ScopeChanged args -> { model with Scope = args.AddedItems.Item 0 :?> Scopes }, Cmd.none
        | AliasesUpdated txt -> { model with Aliases = txt }, Cmd.none
        | QueryChanged txt -> { model with Query = txt }, Cmd.none
        | Search on -> { model with Searching = on }, (if on then searchCmd model else Cmd.none)
        | SearchCompleted -> { model with Searching = false }, Cmd.none
        | Reset -> initModel, Cmd.none

    let internal labeledInput (label: string) ([<ParamArray>] inputs : WidgetBuilder<_,IFabControl> array) =
        //let children =  :: inputs

        let stack = HStack() {
            Label(label)
            for input in inputs -> input
        }

        //for input in inputs do
            //stack.

        stack

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
        Grid(coldefs = [Star], rowdefs = [Auto]) {

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

            (*(Grid(coldefs = [Auto; Star; Auto; Star; Auto], rowdefs = [Auto]) {
                labeledInput().gridColumn(0)
                TextBox(model.Aliases, AliasesUpdated).gridColumn(1)
                TextBlock("for").margin(10, 0).centerVertical().gridColumn(2)
                TextBox(model.Query, QueryChanged).gridColumn(3)
                ToggleButton((if model.Searching then "Stop" else "Search"), model.Searching, Search)
                    .margin(10, 0)
                    .gridColumn(4)
            }).gridRow(1)*)

        }

    
#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model = DesktopApplication(Window(view model))
#endif

    
    let theme = FluentTheme()

    let program = Program.statefulWithCmd init update app
