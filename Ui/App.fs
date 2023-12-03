namespace Ui

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
        | AliasesUpdated txt -> { model with Aliases = txt }, Cmd.none
        | QueryChanged txt -> { model with Query = txt }, Cmd.none
        | Search on -> { model with Searching = on }, (if on then searchCmd model else Cmd.none)
        | SearchCompleted -> { model with Searching = false }, Cmd.none
        | Reset -> initModel, Cmd.none

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
        (VStack() {

            (HStack() {
                (ComboBox(System.Enum.GetValues<Scopes>(), fun scope -> ComboBoxItem(scope.ToString())))
                    .selectedItem(model.Scope)
                TextBox(model.Aliases, AliasesUpdated)
                TextBlock("for").centerVertical()
                TextBox(model.Query, QueryChanged)
                ToggleButton((if model.Searching then "Stop" else "Search"), model.Searching, Search)
            })
                .margin(20.)
                .centerHorizontal()

            Button("Reset", Reset).centerHorizontal()

        })
            .center()

    
#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model = DesktopApplication(Window(view model))
#endif

    
    let theme = FluentTheme()

    let program = Program.statefulWithCmd init update app
