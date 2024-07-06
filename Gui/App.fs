namespace SubTubular.Gui

open System
open System.Collections.Generic
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.Markup.Xaml.Styling
open Avalonia.Media
open AsyncImageLoader
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module App =
    type Commands =
        | ListKeywords = 1
        | Search = 0

    type Model =
        {
            Notifier: WindowNotificationManager

            Settings: Settings.Model
            Cache: Cache.Model
            Recent: ConfigFile.Model
            Command: Commands
            Scopes: Scopes.Model
            ShowScopes: bool
            Query: string

            ResultOptions: ResultOptions.Model

            Running: CancellationTokenSource
            SearchResults: (VideoSearchResult * uint32) list

            /// video IDs by keyword by scope
            KeywordResults: Dictionary<CommandScope, Dictionary<string, List<string>>>

            DisplayOutputOptions: bool
            FileOutput: FileOutput.Model
        }

    type Msg =
        | CacheMsg of Cache.Msg
        | RecentMsg of ConfigFile.Msg
        | SettingsMsg of Settings.Msg
        | CommandChanged of Commands
        | QueryChanged of string
        | ScopesMsg of Scopes.Msg
        | ShowScopesChanged of bool
        | ResultOptionsMsg of ResultOptions.Msg
        | ResultOptionsChanged

        | DisplayOutputOptionsChanged of bool
        | FileOutputMsg of FileOutput.Msg
        | SavedOutput of string

        | Run of bool
        | SearchResults of VideoSearchResult list
        | KeywordResults of (string * string * CommandScope) list
        | CommandCompleted
        | SearchResultMsg of SearchResult.Msg

        | AttachedToVisualTreeChanged of VisualTreeAttachmentEventArgs
        | Notify of string

    let private mapToCommand model =
        let order = ResultOptions.getSearchCommandOrderOptions model.ResultOptions

        let command =
            if model.Command = Commands.Search then
                SearchCommand() :> OutputCommand
            else
                ListKeywords()

        Scopes.setOnCommand model.Scopes command

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

    let private runCmd model =
        fun dispatch ->
            task {
                try
                    let command = mapToCommand model
                    let cancellation = model.Running.Token

                    match command with
                    | :? SearchCommand as search ->
                        Prevalidate.Search search

                        if search.AreScopesValid() |> not then
                            do! RemoteValidate.ScopesAsync(search, Services.Youtube, Services.DataStore, cancellation)

                        if command.SaveAsRecent then
                            dispatch (RecentMsg(ConfigFile.CommandRun command))

                        do!
                            Services.Youtube
                                .SearchAsync(search, cancellation)
                                .dispatchBatchThrottledTo (300, SearchResults, dispatch)

                    | :? ListKeywords as listKeywords ->
                        Prevalidate.Scopes listKeywords

                        if listKeywords.AreScopesValid() |> not then
                            do!
                                RemoteValidate.ScopesAsync(
                                    listKeywords,
                                    Services.Youtube,
                                    Services.DataStore,
                                    cancellation
                                )

                        if command.SaveAsRecent then
                            dispatch (RecentMsg(ConfigFile.CommandRun command))

                        do!
                            Services.Youtube
                                .ListKeywordsAsync(listKeywords, cancellation)
                                .dispatchBatchThrottledTo (
                                    300,
                                    (fun list -> list |> List.map _.ToTuple() |> KeywordResults),
                                    dispatch
                                )

                    | _ -> failwith ("Unknown command type " + command.GetType().ToString())
                with
                | :? InputException as exn -> Notify exn.Message |> dispatch
                | exn -> Notify exn.Message |> dispatch

                dispatch CommandCompleted
            }
            |> Async.AwaitTask
            |> Async.StartImmediate
        |> Cmd.ofEffect

    let private saveOutput model =
        async {
            let command = mapToCommand model

            match command with
            | :? SearchCommand as search ->
                Prevalidate.Search search

                let! path =
                    FileOutput.saveAsync search (fun writer ->
                        for result in model.SearchResults do
                            writer.WriteVideoResult(fst result, snd result))

                return SavedOutput path |> Some

            | :? ListKeywords as listKeywords ->
                Prevalidate.Scopes listKeywords
                let! path = FileOutput.saveAsync listKeywords _.ListKeywords(model.KeywordResults)
                return SavedOutput path |> Some

            | _ -> return None
        }
        |> Cmd.OfAsync.msgOption

    let private notify (notifier: WindowNotificationManager) notification =
        Dispatch.toUiThread (fun () -> notifier.Show(notification))

    let private notifyInfo (notifier: WindowNotificationManager) title =
        notify notifier (Notification(title, "", NotificationType.Information, TimeSpan.FromSeconds 3))
        Cmd.none

    let private initModel =
        { Cache = Cache.initModel
          Settings = Settings.initModel
          Recent = ConfigFile.initModel
          Command = Commands.Search
          Notifier = null
          Query = ""

          Scopes = Scopes.init ()
          ShowScopes = true
          ResultOptions = ResultOptions.initModel

          DisplayOutputOptions = false
          FileOutput = FileOutput.init ()

          Running = null
          SearchResults = []
          KeywordResults = null }

    // load settings on init, see https://docs.fabulous.dev/basics/application-state/commands#triggering-commands-on-initialization
    let private init () =
        // see https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia?tab=readme-ov-file#loaders
        ImageLoader.AsyncImageLoader.Dispose()
        ImageLoader.AsyncImageLoader <- new Loaders.DiskCachedWebImageLoader(Folder.GetPath(Folders.thumbnails))

        initModel,
        Cmd.batch
            [ Settings.load |> Cmd.map SettingsMsg
              ConfigFile.loadRecent |> Cmd.OfTask.msg |> Cmd.map RecentMsg ]

    let private searchTab = ViewRef<TabItem>()
    let private applyResultOptions = Cmd.debounce 300 (fun () -> ResultOptionsChanged)
    let private getResults model = model.SearchResults |> List.map fst

    let private addPadding model list =
        list |> List.map (fun r -> r, uint32 model.ResultOptions.Padding)

    let private requestSaveSettings () =
        Settings.requestSave () |> Cmd.map SettingsMsg

    let private update msg model =
        match msg with

        | CacheMsg cmsg ->
            let cache, cmd = Cache.update cmsg model.Cache
            { model with Cache = cache }, Cmd.map CacheMsg cmd

        | RecentMsg rmsg ->
            let loaded, cmd =
                match rmsg with
                | ConfigFile.Msg.Load cmd ->
                    searchTab.Value.IsSelected <- true
                    let cmdClone = deepClone cmd // to avoid modifying the loaded recent command object itself

                    let updated =
                        match cmdClone with
                        | :? SearchCommand as s ->
                            { model with
                                Command = Commands.Search
                                Query = s.Query
                                ResultOptions =
                                    { model.ResultOptions with
                                        Padding = s.Padding |> int
                                        OrderByScore = s.OrderBy |> Seq.contains SearchCommand.OrderOptions.score
                                        OrderDesc = s.OrderBy |> Seq.contains SearchCommand.OrderOptions.asc |> not } }
                        | :? ListKeywords as l ->
                            { model with
                                Command = Commands.ListKeywords }
                        | _ -> failwith "unsupported command type"

                    let scopes, scopesCmd = Scopes.loadRecentCommand model.Scopes cmdClone

                    { updated with
                        Scopes = scopes
                        ShowScopes = true
                        SearchResults = []
                        KeywordResults = null
                        DisplayOutputOptions = false
                        FileOutput =
                            { updated.FileOutput with
                                To = cmdClone.FileOutputPath
                                Html = cmdClone.OutputHtml
                                Opening =
                                    if cmdClone.Show.HasValue then
                                        match cmdClone.Show.Value with
                                        | OutputCommand.Shows.file -> FileOutput.Open.file
                                        | OutputCommand.Shows.folder -> FileOutput.Open.folder
                                        | _ -> FileOutput.Open.nothing
                                    else
                                        FileOutput.Open.nothing } },
                    Cmd.map ScopesMsg scopesCmd
                | _ -> model, Cmd.none

            let recent, rCmd = ConfigFile.update rmsg model.Recent
            { loaded with Recent = recent }, Cmd.batch [ cmd; Cmd.map RecentMsg rCmd ]

        | CommandChanged cmd -> { model with Command = cmd }, Cmd.none
        | QueryChanged txt -> { model with Query = txt }, Cmd.none

        | ScopesMsg scpsMsg ->
            match scpsMsg with
            | Scopes.Msg.OpenUrl url -> ShellCommands.OpenUri url
            | _ -> ()

            let scopes, cmd = Scopes.update scpsMsg model.Scopes
            { model with Scopes = scopes }, Cmd.map ScopesMsg cmd

        | ShowScopesChanged show -> { model with ShowScopes = show }, Cmd.none

        // udpate ResultOptions and debounce applying them to SearchResults
        | ResultOptionsMsg ext ->
            let options = ResultOptions.update ext model.ResultOptions
            { model with ResultOptions = options }, applyResultOptions ()

        // apply ResultOptions to SearchResults and debounce saving app settings
        | ResultOptionsChanged ->
            { model with
                SearchResults =
                    ResultOptions.orderVideoResults model.ResultOptions (getResults model)
                    |> addPadding model },
            requestSaveSettings ()

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
                | _ -> requestSaveSettings ()

            updated, cmd

        | SavedOutput path -> model, notifyInfo model.Notifier ("Saved results to " + path)

        | Run on ->
            let updated =
                { model with
                    Running = if on then new CancellationTokenSource() else null

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
                            model.KeywordResults }

            updated, (if on then runCmd updated else Cmd.none)

        | SearchResults list ->
            { model with
                SearchResults =
                    getResults model @ list
                    |> ResultOptions.orderVideoResults model.ResultOptions
                    |> addPadding model },
            Cmd.none

        | KeywordResults list ->
            for (keyword, videoId, scope) in list do
                Youtube.AggregateKeywords(keyword, videoId, scope, model.KeywordResults)

            model, Cmd.none

        | CommandCompleted ->
            if model.Running <> null then
                model.Running.Dispose()

            let cmd =
                if model.Command = Commands.Search then
                    "search"
                else
                    "listing keywords"

            { model with
                Running = null
                ShowScopes = false },
            notifyInfo model.Notifier (cmd + " completed")

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

        | SettingsMsg smsg ->
            let upd, cmd = Settings.update smsg model.Settings
            let mappedCmd = Cmd.map SettingsMsg cmd

            match smsg with
            | Settings.Msg.Save ->
                let saved =
                    { upd with
                        ResultOptions = Some model.ResultOptions
                        FileOutput = Some model.FileOutput }

                { model with Settings = saved }, Cmd.batch [ mappedCmd; Settings.save saved |> Cmd.map SettingsMsg ]

            | Settings.Msg.Loaded s ->
                { model with
                    Settings = upd
                    ResultOptions =
                        match s.ResultOptions with
                        | None -> ResultOptions.initModel
                        | Some ro -> ro
                    FileOutput =
                        match s.FileOutput with
                        | None -> FileOutput.init ()
                        | Some fo -> fo },
                mappedCmd

            | _ -> { model with Settings = upd }, mappedCmd

    let private runCommand model =
        (Grid(coldefs = [ Star ], rowdefs = [ Auto; Auto; Auto; Auto; Star ]) {
            let isSearch = model.Command = Commands.Search

            let hasResults =
                if isSearch then
                    not model.SearchResults.IsEmpty
                else
                    model.KeywordResults <> null && model.KeywordResults.Count > 0

            // scopes
            ScrollViewer(View.map ScopesMsg (Scopes.view model.Scopes))
                .card()
                .isVisible (model.ShowScopes)

            // see https://usecasepod.github.io/posts/mvu-composition.html
            // and https://github.com/TimLariviere/FabulousContacts/blob/0d5024c4bfc7a84f02c0788a03f63ff946084c0b/FabulousContacts/ContactsListPage.fs#L89C17-L89C31
            // search options
            (Grid(coldefs = [ Auto; Star; Auto; Auto; Auto ], rowdefs = [ Auto ]) {
                Menu() {
                    MenuItem("🏷 List _keywords", CommandChanged Commands.ListKeywords)
                        .tooltip(ListKeywords.Description)
                        .tapCursor()
                        .asToggle (not isSearch)

                    MenuItem("🔍 _Search for", CommandChanged Commands.Search)
                        .tooltip(SearchCommand.Description)
                        .tapCursor()
                        .asToggle (isSearch)
                }

                TextBox(model.Query, QueryChanged)
                    .watermark("your query")
                    .isVisible(isSearch)
                    .gridColumn (1)

                Label("in").centerVertical().gridColumn (2)

                ToggleButton(
                    (if model.ShowScopes then
                         "👆 these scopes"
                     else
                         $"✊ {model.Scopes.List.Length} scopes"),
                    model.ShowScopes,
                    ShowScopesChanged
                )
                    .gridColumn (3)

                let isRunning = model.Running <> null

                ToggleButton((if isRunning then "✋ _Hold up!" else "👉 _Hit it!"), isRunning, Run)
                    .fontSize(16)
                    .margin(10, 0)
                    .gridColumn (4)
            })
                .card()
                .gridRow (1)

            // result options
            (Grid(coldefs = [ Auto; Star; Star; Auto ], rowdefs = [ Auto; Auto ]) {
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

                // output options
                (View.map FileOutputMsg (FileOutput.view model.FileOutput))
                    .isVisible(model.DisplayOutputOptions)
                    .gridRow(1)
                    .gridColumnSpan (4)
            })
                .card()
                .isVisible(hasResults)
                .gridRow (2)

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
                                            .background(ThemeAware.With(Colors.Thistle, Colors.Purple))
                                            .cornerRadius(2)
                                            .padding(3, 0, 3, 0)
                                            .margin (3)
                                }
                            })
                                .trailingMargin ()

                    if isSearch && hasResults then
                        ListBox(
                            model.SearchResults,
                            (fun item ->
                                let result, padding = item
                                View.map SearchResultMsg (SearchResult.render padding result))
                        )
                })
            )
                .isVisible(hasResults)
                .card()
                .gridRow (4)
        })
            .margin(5, 5, 5, 0)
            .onAttachedToVisualTree (AttachedToVisualTreeChanged)

    let private view model =
        TabControl() {
            TabItem("🕝 Recent", View.map RecentMsg (ConfigFile.view model.Recent))
            TabItem("🔍 Search", runCommand model).reference (searchTab)
            TabItem("🗃 Storage", View.map CacheMsg (Cache.view model.Cache))
            TabItem("⚙ Settings", View.map SettingsMsg (Settings.view model.Settings))
        }

#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model =
        let window =
            Window(view model)
                .icon(avaloniaResourceUri ("SubTubular.ico"))
                .title("SubTubular")
                .background (ThemeAware.With(Colors.BlanchedAlmond, Colors.MidnightBlue))

        DesktopApplication(window)
            .requestedThemeVariant(Settings.getThemeVariant (model.Settings.ThemeVariantKey))
#if DEBUG
            .attachDevTools ()
#endif
#endif

    let create () =
        let theme () =
            StyleInclude(baseUri = null, Source = Uri(avaloniaResourceUri ("Styles.xaml")))

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
