﻿namespace Ui

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.Media
open Avalonia.Themes.Fluent
open AsyncImageLoader
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open SubTubular.Extensions
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

            Recent: ConfigFile.Model
            Command: Commands
            Scopes: Scopes.Model
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
        | RecentMsg of ConfigFile.Msg
        | CommandChanged of Commands
        | QueryChanged of string
        | ScopesMsg of Scopes.Msg
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
        { Recent = ConfigFile.initModel
          Command = Commands.Search
          Notifier = null
          Query = ""

          Scopes = Scopes.init ()
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

        initModel, Cmd.batch [ Settings.load; ConfigFile.loadRecent |> Cmd.OfTask.msg |> Cmd.map RecentMsg ]

    let private searchTab = ViewRef<TabItem>()
    let private applyResultOptions = Cmd.debounce 300 (fun () -> ResultOptionsChanged)
    let private getResults model = model.SearchResults |> List.map fst

    let private addPadding model list =
        list |> List.map (fun r -> r, uint32 model.ResultOptions.Padding)

    let private update msg model =
        match msg with

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
            let updated =
                { model with
                    Running = if on then new CancellationTokenSource() else null
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

            { model with Running = null }, notifyInfo model.Notifier (cmd + " completed")

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
    let private runCommand model =
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
            (Grid(coldefs = [ Auto; Star; Auto ], rowdefs = [ Auto ]) {
                Menu() {
                    MenuItem("🏷 List _keywords", CommandChanged Commands.ListKeywords)
                        .asToggle (not isSearch)

                    MenuItem("🔍 _Search for", CommandChanged Commands.Search).asToggle (isSearch)
                }

                TextBox(model.Query, QueryChanged)
                    .watermark("your query")
                    .isVisible(isSearch)
                    .gridColumn (1)

                let isRunning = model.Running <> null

                ToggleButton((if isRunning then "✋ _Hold up!" else "👉 _Hit it!"), isRunning, Run)
                    .fontSize(16)
                    .margin(10, 0)
                    .gridColumn (2)
            })
                .trailingMargin(4)
                .gridRow (1)

            // scopes
            ScrollViewer(View.map ScopesMsg (Scopes.view model.Scopes)).trailingMargin (4)

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
                .precedingSeparator(4)
                .trailingMargin(4)
                .isVisible(hasResults)
                .gridRow (2)

            // output options
            (View.map FileOutputMsg (FileOutput.view model.FileOutput))
                .trailingMargin(4)
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
                        ListBox(
                            model.SearchResults,
                            (fun item ->
                                let result, padding = item
                                View.map SearchResultMsg (SearchResult.render padding result))
                        )
                })
            )
                .isVisible(hasResults)
                .precedingSeparator(4)
                .gridRow (4)
        })
            .margin(5, 5, 5, 0)
            .onAttachedToVisualTree (AttachedToVisualTreeChanged)

    let private view model =
        TabControl() {
            TabItem("🕝 Recent", View.map RecentMsg (ConfigFile.view model.Recent))
            TabItem("🔍 Search", runCommand model).reference (searchTab)
        }
#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model =
        DesktopApplication(Window(view model).icon("avares://Ui/SubTubular.ico").title ("SubTubular"))
#if DEBUG
            .attachDevTools ()
#endif
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
