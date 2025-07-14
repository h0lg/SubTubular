﻿namespace SubTubular.Gui

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
        { OrderByScore: bool
          OrderDesc: bool
          Padding: int

          FileOutput: FileOutput.Model option }

    type Model =
        { Notifier: WindowNotificationManager

          Scopes: Scopes.Model
          Query: string

          OrderByScore: bool
          OrderDesc: bool
          Padding: int

          Searching: bool
          SearchResults: VideoSearchResult list

          DisplayOutputOptions: bool
          FileOutput: FileOutput.Model }

    type Msg =
        | QueryChanged of string
        | ScopesMsg of Scopes.Msg
        | OrderByScoreChanged of bool
        | OrderDescChanged of bool
        | PaddingChanged of float option

        | DisplayOutputOptionsChanged of bool
        | FileOutputMsg of FileOutput.Msg
        | SavedOutput of string

        | Search of bool
        | SearchResult of VideoSearchResult
        | SearchProgress of BatchProgress
        | SearchCompleted

        | AttachedToVisualTreeChanged of VisualTreeAttachmentEventArgs
        | Notify of string
        | SearchResultMsg of SearchResult.Msg
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
                else
                    return Reset
            }
            |> Cmd.OfAsync.msg

        let save model =
            async {
                let settings =
                    { OrderByScore = model.OrderByScore
                      OrderDesc = model.OrderDesc
                      Padding = model.Padding

                      FileOutput = Some model.FileOutput }

                let json = JsonSerializer.Serialize settings
                do! File.WriteAllTextAsync(path, json) |> Async.AwaitTask
                return SettingsSaved // Cmd.none?
            }
            |> Cmd.OfAsync.msg

    let private mapToSearchCommand model =
        let order =
            match (model.OrderByScore, model.OrderDesc) with
            | (true, true) -> [ SearchCommand.OrderOptions.score ]
            | (true, false) -> [ SearchCommand.OrderOptions.score; SearchCommand.OrderOptions.asc ]
            | (false, true) -> [ SearchCommand.OrderOptions.uploaded ]
            | (false, false) -> [ SearchCommand.OrderOptions.uploaded; SearchCommand.OrderOptions.asc ]

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
        command.Padding <- model.Padding |> uint16
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
                    CmdExtensions.bufferedThrottle 100 (fun progress -> SearchProgress progress)

                command.SetProgressReporter(
                    Progress<BatchProgress>(fun progress ->
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

    let private orderResults model =
        let sortBy =
            if model.OrderDesc then
                List.sortByDescending
            else
                List.sortBy

        let comparable: (VideoSearchResult -> IComparable) =
            if model.OrderByScore then
                (fun r -> r.Score)
            else
                (fun r -> r.Video.Uploaded)

        model.SearchResults |> sortBy comparable

    let private saveOutput model =
        async {
            let command = mapToSearchCommand model
            let! path = FileOutput.saveAsync command (orderResults model)
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

          OrderByScore = true
          OrderDesc = true

          Padding = 69
          DisplayOutputOptions = false
          FileOutput = FileOutput.init ()

          Searching = false
          SearchResults = [] }

    // load settings on init, see https://docs.fabulous.dev/basics/application-state/commands#triggering-commands-on-initialization
    let init () = initModel, Settings.load

    let update msg model =
        match msg with
        | QueryChanged txt -> { model with Query = txt }, Cmd.none
        | OrderByScoreChanged value -> { model with OrderByScore = value }, Settings.requestSave ()
        | OrderDescChanged value -> { model with OrderDesc = value }, Settings.requestSave ()

        | ScopesMsg ext ->
            let scopes = Scopes.update ext model.Scopes
            { model with Scopes = scopes }, Cmd.none


        | PaddingChanged padding ->
            { model with
                Padding = int padding.Value },
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

        | Search on ->
            { model with
                Searching = on
                SearchResults = [] },
            (if on then searchCmd model else Cmd.none)

        | SearchResult result ->
            { model with
                SearchResults = result :: model.SearchResults },
            Cmd.none

        | SearchProgress progress ->
            let scopes = Scopes.updateSearchProgress progress model.Scopes
            { model with Scopes = scopes }, Cmd.none

        | SearchCompleted -> { model with Searching = false }, notifyInfo model.Notifier "search completed"

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
                OrderByScore = s.OrderByScore
                OrderDesc = s.OrderDesc
                Padding = s.Padding
                FileOutput =
                    match s.FileOutput with
                    | None -> FileOutput.init ()
                    | Some fo -> fo },
             Cmd.none)

        | Reset -> initModel, Cmd.none

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

                (HStack(5) {
                    let direction =
                        match (model.OrderByScore, model.OrderDesc) with
                        | (true, true) -> "⋱ highest"
                        | (true, false) -> "⋰ lowest"
                        | (false, true) -> "⋱ latest"
                        | (false, false) -> "⋰ earliest"

                    Label "ordered by"
                    ToggleButton(direction, model.OrderDesc, OrderDescChanged)

                    ToggleButton(
                        (if model.OrderByScore then "💯 score" else "📅 uploaded"),
                        model.OrderByScore,
                        OrderByScoreChanged
                    )

                    Label "first"
                })
                    .gridColumn(1)
                    .centerVertical()
                    .centerHorizontal ()

                (HStack(5) {
                    Label "padded with"

                    NumericUpDown(0, float UInt16.MaxValue, Some(float model.Padding), PaddingChanged)
                        .increment(5)
                        .formatString("F0")
                        .tip (ToolTip("how much context to show a search result in"))

                    Label "chars for context"
                })
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
                    for result in orderResults model do
                        (View.map SearchResultMsg (SearchResult.render (model.Padding |> uint32) result))
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
