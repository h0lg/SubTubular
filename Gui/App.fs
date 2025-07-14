﻿namespace SubTubular.Gui

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open Avalonia
open Avalonia.Controls.Notifications
open Avalonia.Media
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

    type Commands =
        | ListKeywords = 1
        | Search = 0

    type Model =
        {
            Notifier: WindowNotificationManager

            Command: Commands
            Scopes: Scopes.Model
            Query: string

            ResultOptions: ResultOptions.Model

            Running: bool
            SearchResults: VideoSearchResult list

            /// video IDs by keyword by scope
            KeywordResults: Dictionary<CommandScope, Dictionary<string, List<string>>>

            DisplayOutputOptions: bool
            FileOutput: FileOutput.Model
        }

    type Msg =
        | CommandChanged of Commands
        | QueryChanged of string
        | ScopesMsg of Scopes.Msg
        | ResultOptionsMsg of ResultOptions.Msg

        | DisplayOutputOptionsChanged of bool
        | FileOutputMsg of FileOutput.Msg
        | SavedOutput of string

        | Run of bool
        | SearchResult of VideoSearchResult
        | KeywordResult of string * string * CommandScope
        | CommandProgress of BatchProgress list
        | CommandCompleted
        | SearchResultMsg of SearchResult.Msg

        | AttachedToVisualTreeChanged of VisualTreeAttachmentEventArgs
        | Notify of string
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
                do! File.WriteAllTextAsync(path, json) |> Async.AwaitTask
                return SettingsSaved
            }
            |> Cmd.OfAsync.msg

    let private mapToCommand model =
        let order = ResultOptions.getSearchCommandOrderOptions model.ResultOptions

        let command =
            if model.Command = Commands.Search then
                SearchCommand() :> OutputCommand
            else
                ListKeywords()

        let getScopes scope =
            model.Scopes.List
            |> List.filter (fun s -> s.Type = scope && s.Aliases.IsNonWhiteSpace())

        command.Channels <-
            getScopes Scope.Type.channel
            |> List.map (fun scope ->
                ChannelScope(
                    Scope.cleanAlias scope.Aliases,
                    scope.Top.Value |> uint16,
                    scope.CacheHours.Value |> float32
                ))
            |> List.toArray

        command.Playlists <-
            getScopes Scope.Type.playlist
            |> List.map (fun scope ->
                PlaylistScope(
                    Scope.cleanAlias scope.Aliases,
                    scope.Top.Value |> uint16,
                    scope.CacheHours.Value |> float32
                ))
            |> List.toArray

        let videos = getScopes Scope.Type.videos |> List.tryExactlyOne

        if videos.IsSome then
            command.Videos <- VideosScope(videos.Value.Aliases.Split [| ',' |] |> Array.map Scope.cleanAlias)

        match command with
        | :? SearchCommand as search ->
            search.Query <- model.Query
            search.Padding <- model.ResultOptions.Padding |> uint16
            search.OrderBy <- order
        | _ -> ()

        command.OutputHtml <- model.FileOutput.Html
        command.FileOutputPath <- model.FileOutput.To

        if model.FileOutput.Opening = FileOutput.Open.file then
            command.Show <- OutputCommand.Shows.file
        elif model.FileOutput.Opening = FileOutput.Open.folder then
            command.Show <- OutputCommand.Shows.folder

        command

    let private cacheFolder = Folder.GetPath Folders.cache
    let private dataStore = JsonFileDataStore cacheFolder
    let private youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)

    let dispatchAsyncEnumerable
        (action: Collections.Generic.IAsyncEnumerable<'result>)
        (map: 'result -> Msg)
        (dispatch: Msg -> unit)
        =
        async {
            let results = action.GetAsyncEnumerator()

            let rec dispatchResults () =
                async {
                    let! hasNext = results.MoveNextAsync().AsTask() |> Async.AwaitTask

                    if hasNext then
                        map results.Current |> dispatch
                        do! dispatchResults ()
                }

            do! dispatchResults ()
        }

    let private searchCmd model =
        fun dispatch ->
            async {
                let command = mapToCommand model

                let dispatchProgress, awaitNextDispatch =
                    CmdExtensions.batchedThrottle 100 (fun progresses -> CommandProgress progresses)

                command.SetProgressReporter(
                    Progress<BatchProgress>(fun progress ->
                        // make dispatch available to commands
                        dispatchProgress progress |> List.iter (fun effect -> effect dispatch))
                )

                match command with
                | :? SearchCommand as search ->
                    CommandValidator.PrevalidateSearchCommand search
                    use cts = new CancellationTokenSource()

                    do!
                        CommandValidator.ValidateScopesAsync(search, youtube, dataStore, cts.Token)
                        |> Async.AwaitTask

                    if command.SaveAsRecent then
                        do! RecentCommand.SaveAsync(command) |> Async.AwaitTask

                    do! dispatchAsyncEnumerable (youtube.SearchAsync(search, cts.Token)) SearchResult dispatch

                | :? ListKeywords as listKeywords ->
                    CommandValidator.PrevalidateScopes listKeywords
                    use cts = new CancellationTokenSource()

                    do!
                        CommandValidator.ValidateScopesAsync(listKeywords, youtube, dataStore, cts.Token)
                        |> Async.AwaitTask

                    if command.SaveAsRecent then
                        do! RecentCommand.SaveAsync(command) |> Async.AwaitTask

                    do!
                        dispatchAsyncEnumerable
                            (youtube.ListKeywordsAsync(listKeywords, cts.Token))
                            (fun result -> KeywordResult(result.ToTuple()))
                            dispatch

                | _ -> failwith ("Unknown command type " + command.GetType().ToString())

                do! awaitNextDispatch (Some(TimeSpan.FromMilliseconds 100)) // to make sure all progresses are dispatched before calling it done

                dispatch CommandCompleted
            }
            |> Async.StartImmediate
        |> Cmd.ofEffect

    let private saveOutput model =
        async {
            let command = mapToCommand model

            match command with
            | :? SearchCommand as search ->
                CommandValidator.PrevalidateSearchCommand search

                let! path =
                    FileOutput.saveAsync search (fun writer ->
                        for result in model.SearchResults do
                            writer.WriteVideoResult(result, search.Padding |> uint32))

                return SavedOutput path |> Some

            | :? ListKeywords as listKeywords ->
                CommandValidator.PrevalidateScopes listKeywords
                let! path = FileOutput.saveAsync listKeywords _.ListKeywords(model.KeywordResults)
                return SavedOutput path |> Some

            | _ -> return None
        }
        |> Cmd.OfAsync.msgOption

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
        { Command = Commands.Search
          Notifier = null
          Query = ""

          Scopes = Scopes.init youtube
          ResultOptions = ResultOptions.initModel

          DisplayOutputOptions = false
          FileOutput = FileOutput.init ()

          Running = false
          SearchResults = []
          KeywordResults = null }

    // load settings on init, see https://docs.fabulous.dev/basics/application-state/commands#triggering-commands-on-initialization
    let init () = initModel, Settings.load

    let update msg model =
        match msg with
        | CommandChanged cmd -> { model with Command = cmd }, Cmd.none
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

        | SavedOutput path -> model, notifyInfo model.Notifier ("Saved results to " + path)

        | Run on ->
            { model with
                Running = on

                // reset result collections when starting a new search
                SearchResults =
                    if on && model.Command = Commands.Search then
                        []
                    else
                        model.SearchResults
                KeywordResults =
                    if on && model.Command = Commands.ListKeywords then
                        Dictionary<CommandScope, Dictionary<string, List<string>>>()
                    else
                        model.KeywordResults },
            (if on then searchCmd model else Cmd.none)

        | SearchResult result ->
            { model with
                SearchResults =
                    result :: model.SearchResults
                    |> ResultOptions.orderVideoResults model.ResultOptions },
            Cmd.none

        | KeywordResult(keyword, videoId, scope) ->
            Youtube.AggregateKeywords(keyword, videoId, scope, model.KeywordResults)

            { model with
                KeywordResults = model.KeywordResults },
            Cmd.none

        | CommandProgress progresses ->
            let scopes = Scopes.updateSearchProgress progresses model.Scopes
            { model with Scopes = scopes }, Cmd.none

        | CommandCompleted ->
            let cmd =
                if model.Command = Commands.Search then
                    "search"
                else
                    "listing keywords"

            { model with Running = false }, notifyInfo model.Notifier (cmd + " completed")

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

    let view model =
        (Grid(coldefs = [ Star ], rowdefs = [ Auto; Auto; Auto; Auto; Star ]) {
            let isSearch = model.Command = Commands.Search

            let hasResults =
                if isSearch then
                    not model.SearchResults.IsEmpty
                else
                    model.KeywordResults <> null && model.KeywordResults.Count > 0

            // see https://usecasepod.github.io/posts/mvu-composition.html
            // and https://github.com/TimLariviere/FabulousContacts/blob/0d5024c4bfc7a84f02c0788a03f63ff946084c0b/FabulousContacts/ContactsListPage.fs#L89C17-L89C31
            // search options
            (Grid(coldefs = [ Auto; Star; Auto; Stars 2; Auto ], rowdefs = [ Auto ]) {
                Button("🏷 List keywords in", CommandChanged Commands.ListKeywords)
                    .asToggle(not isSearch)
                    .gridColumn (1)

                Button("🔍 Search for", CommandChanged Commands.Search)
                    .asToggle(isSearch)
                    .gridColumn (2)

                TextBox(model.Query, QueryChanged)
                    .watermark("your query")
                    .isVisible(isSearch)
                    .gridColumn (3)

                ToggleButton((if model.Running then "⏹⏹️🛑 Stop" else "▶️▶🐎 Run"), model.Running, Run)
                    .margin(10, 0)
                    .gridColumn (4)
            })
                .trailingMargin ()

            // scopes
            ScrollViewer(View.map ScopesMsg (Scopes.view model.Scopes))
                .trailingMargin()
                .gridRow (1)

            // result options
            (Grid(coldefs = [ Auto; Star; Star; Auto ], rowdefs = [ Auto ]) {
                TextBlock("Results")

                (View.map ResultOptionsMsg (ResultOptions.orderBy model.ResultOptions))
                    .centerVertical()
                    .centerHorizontal()
                    .isVisible(isSearch)
                    .gridColumn (1)

                (View.map ResultOptionsMsg (ResultOptions.padding model.ResultOptions))
                    .centerHorizontal()
                    .isVisible(isSearch)
                    .gridColumn (2)

                ToggleButton("to file 📄", model.DisplayOutputOptions, DisplayOutputOptionsChanged)
                    .gridColumn (3)
            })
                .trailingMargin()
                .isVisible(hasResults)
                .gridRow (2)

            // output options
            (View.map FileOutputMsg (FileOutput.view model.FileOutput))
                .trailingMargin()
                .isVisible(model.DisplayOutputOptions)
                .gridRow (3)

            // results
            ScrollViewer(
                (VStack() {
                    if not isSearch && hasResults then
                        for scope in model.KeywordResults do
                            (VStack() {
                                TextBlock(scope.Key.Describe().Join(" "))

                                HWrap() {
                                    for keyword in Youtube.OrderKeywords scope.Value do
                                        TextBlock(keyword.Value.Count.ToString() + "x")

                                        Border(TextBlock(keyword.Key))
                                            .background(Colors.Purple)
                                            .cornerRadius(2)
                                            .padding(3, 0, 3, 0)
                                            .margin (3)
                                }
                            })
                                .trailingMargin ()

                    if isSearch && hasResults then
                        for result in model.SearchResults do
                            (View.map
                                SearchResultMsg
                                (SearchResult.render (model.ResultOptions.Padding |> uint32) result))
                                .trailingMargin ()
                })
            )
                .isVisible(hasResults)
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
