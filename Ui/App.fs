namespace Ui

open System
open System.IO
open System.Text.Json
open System.Threading
open Avalonia
open Avalonia.Controls.Notifications
open Avalonia.Themes.Fluent
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open SubTubular.Extensions
open Styles
open type Fabulous.Avalonia.View

module App =
    type SavedSettings =
        { ResultOptions: ResultOptions.Model option
          FileOutput: FileOutput.Model option }

    type Model =
        { Notifier: WindowNotificationManager

          Scopes: Scopes.Model
          Query: string

          ResultOptions: ResultOptions.Model

          Searching: bool
          SearchResults: VideoSearchResult list

          DisplayOutputOptions: bool
          FileOutput: FileOutput.Model }

    type Msg =
        | QueryChanged of string
        | ScopesMsg of Scopes.Msg
        | ResultOptionsMsg of ResultOptions.Msg

        | DisplayOutputOptionsChanged of bool
        | FileOutputMsg of FileOutput.Msg
        | SavedOutput of string

        | Search of bool
        | SearchResult of VideoSearchResult
        | SearchProgress of BatchProgress
        | SearchCompleted
        | SearchResultMsg of SearchResult.Msg

        | AttachedToVisualTreeChanged of VisualTreeAttachmentEventArgs
        | Notify of string
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
                    return SettingsLoaded settings |> Some
                else
                    return None
            }
            |> Cmd.OfAsync.msgOption

        let save model =
            async {
                let settings =
                    { ResultOptions = Some model.ResultOptions
                      FileOutput = Some model.FileOutput }

                let json = JsonSerializer.Serialize settings
                do! File.WriteAllTextAsync(getPath, json) |> Async.AwaitTask
                return SettingsSaved
            }
            |> Cmd.OfAsync.msg

    let private mapToSearchCommand model =
        let order = ResultOptions.getSearchCommandOrderOptions model.ResultOptions
        let command = SearchCommand()

        let getScopes scope =
            model.Scopes.List
            |> List.filter (fun s -> s.Type = scope && s.Aliases.IsNonWhiteSpace())

        command.Channels <-
            getScopes Scopes.Type.channel
            |> List.map (fun scope ->
                ChannelScope(scope.Aliases, scope.Top.Value |> uint16, scope.CacheHours.Value |> float32))
            |> List.toArray

        command.Playlists <-
            getScopes Scopes.Type.playlist
            |> List.map (fun scope ->
                PlaylistScope(scope.Aliases, scope.Top.Value |> uint16, scope.CacheHours.Value |> float32))
            |> List.toArray

        let videos = getScopes Scopes.Type.videos |> List.tryExactlyOne

        if videos.IsSome then
            command.Videos <- VideosScope(videos.Value.Aliases.Split [| ' ' |])

        command.Query <- model.Query
        command.Padding <- model.ResultOptions.Padding |> uint16
        command.OrderBy <- order
        command.OutputHtml <- model.FileOutput.Html
        command.FileOutputPath <- model.FileOutput.To

        if model.FileOutput.Opening = FileOutput.Open.file then
            command.Show <- OutputCommand.Shows.file
        elif model.FileOutput.Opening = FileOutput.Open.folder then
            command.Show <- OutputCommand.Shows.folder

        command

    let private searchCmd model =
        fun dispatch ->
            async {
                let command = mapToSearchCommand model
                let cacheFolder = Folder.GetPath Folders.cache
                let dataStore = JsonFileDataStore cacheFolder
                let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)

                let dispatchProgress =
                    CmdExtensions.bufferedThrottle 100 (fun progress ->
                        System.Diagnostics.Debug.WriteLine(
                            "############# progress dispatched" + Environment.NewLine + progress.ToString()
                        )

                        SearchProgress progress)

                command.SetProgressReporter(
                    Progress<BatchProgress>(fun progress ->
                        System.Diagnostics.Debug.WriteLine(
                            "############# progress reported" + Environment.NewLine + progress.ToString()
                        )

                        dispatchProgress progress |> List.iter (fun effect -> effect dispatch))
                )

                CommandValidator.PrevalidateSearchCommand command
                use cts = new CancellationTokenSource()

                do!
                    CommandValidator.ValidateScopesAsync(command, youtube, dataStore, cts.Token)
                    |> Async.AwaitTask

                do!
                    youtube.SearchAsync(command, cts.Token)
                    // see https://github.com/fsprojects/FSharp.Control.TaskSeq
                    |> TaskSeq.iter (fun result -> SearchResult result |> dispatch)
                    |> Async.AwaitTask

                dispatch SearchCompleted
            }
            |> Async.StartImmediate
        |> Cmd.ofEffect

    let private saveOutput model =
        async {
            let command = mapToSearchCommand model
            let! path = FileOutput.saveAsync command model.SearchResults
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

    let initModel =
        { Notifier = null
          Query = ""

          Scopes = Scopes.initModel
          ResultOptions = ResultOptions.initModel

          DisplayOutputOptions = false
          FileOutput = FileOutput.init ()

          Searching = false
          SearchResults = [] }

    // load settings on init, see https://docs.fabulous.dev/basics/application-state/commands#triggering-commands-on-initialization
    let init () = initModel, Settings.load

    let update msg model =
        match msg with
        | QueryChanged txt -> { model with Query = txt }, Cmd.none

        | ScopesMsg ext ->
            let scopes = Scopes.update ext model.Scopes
            { model with Scopes = scopes }, Cmd.none

        | ResultOptionsMsg ext ->
            let options = ResultOptions.update ext model.ResultOptions

            { model with
                ResultOptions = options
                SearchResults = ResultOptions.orderVideoResults options model.SearchResults },
            Settings.requestSave ()

        | DisplayOutputOptionsChanged output ->
            { model with
                DisplayOutputOptions = output },
            Cmd.none

        | FileOutputMsg fom ->
            let updated =
                { model with
                    FileOutput = FileOutput.update fom model.FileOutput }

            let cmd =
                match fom with
                | FileOutput.Msg.SaveOutput -> saveOutput updated
                | _ -> Settings.requestSave ()

            updated, cmd

        | SavedOutput path -> model, Notify("Saved results to " + path) |> Cmd.ofMsg

        | Search on ->
            { model with
                Searching = on
                SearchResults = [] },
            (if on then searchCmd model else Cmd.none)

        | SearchResult result ->
            { model with
                SearchResults =
                    result :: model.SearchResults
                    |> ResultOptions.orderVideoResults model.ResultOptions },
            Cmd.none

        | SearchProgress progress ->
            let scopes = Scopes.updateSearchProgress progress model.Scopes
            { model with Scopes = scopes }, Cmd.none

        | SearchCompleted -> { model with Searching = false }, Notify "search completed" |> Cmd.ofMsg

        | SearchResultMsg srm ->
            match srm with
            | SearchResult.Msg.OpenUrl url -> ShellCommands.OpenUri url
            | _ -> ()

            model, Cmd.none

        | AttachedToVisualTreeChanged args ->
            let notifier = FabApplication.Current.WindowNotificationManager
            notifier.Position <- NotificationPosition.BottomRight
            { model with Notifier = notifier }, Cmd.none

        | Notify title -> model, notifyInfo model.Notifier title
        | SaveSettings -> model, Settings.save model
        | SettingsSaved -> model, Cmd.none

        | SettingsLoaded s ->
            ({ model with
                ResultOptions =
                    match s.ResultOptions with
                    | None -> ResultOptions.initModel
                    | Some ro -> ro
                FileOutput =
                    match s.FileOutput with
                    | None -> FileOutput.init ()
                    | Some fo -> fo },
             Cmd.none)

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
        (Grid(coldefs = [ Star ], rowdefs = [ Auto; Auto; Auto; Auto; Star ]) {

            // see https://usecasepod.github.io/posts/mvu-composition.html
            // and https://github.com/TimLariviere/FabulousContacts/blob/0d5024c4bfc7a84f02c0788a03f63ff946084c0b/FabulousContacts/ContactsListPage.fs#L89C17-L89C31
            // search options
            (Grid(coldefs = [ Auto; Star; Auto; Stars 2; Auto ], rowdefs = [ Auto ]) {
                TextBlock("for").margin(10, 0).centerVertical().gridColumn (2)
                TextBox(model.Query, QueryChanged).watermark("your query").gridColumn (3)

                ToggleButton((if model.Searching then "🛑 Stop" else "🔍 Search"), model.Searching, Search)
                    .margin(10, 0)
                    .gridColumn (4)
            })
                .trailingMargin ()

            // scopes
            ScrollViewer(View.map ScopesMsg (Scopes.view model.Scopes))
                .gridRow(1)
                .trailingMargin ()

            // result options
            (Grid(coldefs = [ Auto; Star; Star; Auto ], rowdefs = [ Auto ]) {
                TextBlock("Results")

                (View.map ResultOptionsMsg (ResultOptions.orderBy model.ResultOptions))
                    .gridColumn(1)
                    .centerVertical()
                    .centerHorizontal ()


                (View.map ResultOptionsMsg (ResultOptions.padding model.ResultOptions))
                    .gridColumn(2)
                    .centerHorizontal ()

                ToggleButton("to file 📄", model.DisplayOutputOptions, DisplayOutputOptionsChanged)
                    .gridColumn (3)
            })
                .gridRow(2)
                .trailingMargin()
                .isVisible (not model.SearchResults.IsEmpty)

            // output options
            (View.map FileOutputMsg (FileOutput.view model.FileOutput))
                .isVisible(model.DisplayOutputOptions)
                .gridRow(3)
                .trailingMargin ()

            // results
            ScrollViewer(
                (VStack() {
                    for result in model.SearchResults do
                        (View.map SearchResultMsg (SearchResult.render (model.ResultOptions.Padding |> uint32) result))
                            .trailingMargin ()
                })
            )
                .gridRow (4)
        })
            .margin(5, 5, 5, 0)
            .onAttachedToVisualTree (AttachedToVisualTreeChanged)

#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model = DesktopApplication(Window(view model))
#endif

    let create () =
        let theme () = FluentTheme()

        let program =
            Program.statefulWithCmd init update
            |> Program.withTrace (fun (format, args) -> System.Diagnostics.Debug.WriteLine(format, box args))
            |> Program.withExceptionHandler (fun ex ->
#if DEBUG
                printfn $"Exception: %s{ex.ToString()}"
                false
#else
                true
#endif
            )
            |> Program.withView app

        FabulousAppBuilder.Configure(theme, program)
