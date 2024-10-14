﻿namespace Ui

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Notifications
open Avalonia.Controls.Primitives
open Avalonia.Markup.Xaml.Styling
open Avalonia.Media
open AsyncImageLoader
open Fabulous
open Fabulous.Avalonia
open SubTubular
open type Fabulous.Avalonia.View

module App =
    type Model =
        { Search: OutputCommands.Model
          Settings: Settings.Model
          Cache: Cache.Model
          Recent: ConfigFile.Model }

    type Msg =
        | CacheMsg of Cache.Msg
        | RecentMsg of ConfigFile.Msg
        | SettingsMsg of Settings.Msg
        | SearchMsg of OutputCommands.Msg

        | AttachedToVisualTreeChanged of VisualTreeAttachmentEventArgs
        | Common of CommonMsg

    let private initModel =
        { Cache = Cache.initModel
          Settings = Settings.initModel
          Recent = ConfigFile.initModel
          Search = OutputCommands.initModel }

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
                    let updated, cmd = OutputCommands.load cmdClone model.Search
                    { model with Search = updated }, Cmd.map SearchMsg cmd

                | ConfigFile.Msg.Common msg -> model, Common msg |> Cmd.ofMsg
                | _ -> model, Cmd.none

            let recent, rCmd = ConfigFile.update rmsg model.Recent
            { loaded with Recent = recent }, Cmd.batch [ cmd; Cmd.map RecentMsg rCmd ]

        | AttachedToVisualTreeChanged args ->
            let notifier = FabApplication.Current.WindowNotificationManager
            notifier.Position <- NotificationPosition.BottomRight
            Notify.via <- notifier
            model, Cmd.none

        | SearchMsg smsg ->
            let fwdCmd =
                match smsg with
                | OutputCommands.Msg.Common msg -> Common msg |> Cmd.ofMsg
                | OutputCommands.Msg.CommandValidated cmd -> ConfigFile.CommandRun cmd |> RecentMsg |> Cmd.ofMsg
                | OutputCommands.Msg.ResultOptionsChanged -> requestSaveSettings ()
                | OutputCommands.Msg.FileOutputMsg fom ->
                    match fom with
                    | FileOutput.Msg.SaveOutput -> Cmd.none
                    | _ -> requestSaveSettings ()
                | _ -> Cmd.none

            let updated, cmd = OutputCommands.update smsg model.Search
            { model with Search = updated }, Cmd.batch [ fwdCmd; Cmd.map SearchMsg cmd ]

        | Common cmsg ->
            match cmsg with
            | Notify title -> model, Notify.info title "" (TimeSpan.FromSeconds 3)
            | NotifyLong(title, message) -> model, Notify.info title message Notify.nonExpiring
            | Fail title -> model, Notify.error title ""
            | FailLong(title, message) -> model, Notify.error title message
            | CopyShellCmd cmd -> model, copyShellCmd cmd |> Cmd.OfTask.msg |> Cmd.map Common

            | OpenUrl url ->
                ShellCommands.OpenUri url
                model, Cmd.none

            | ToggleFlyout args ->
                let control = unbox<Control> args.Source
                FlyoutBase.ShowAttachedFlyout(control)
                model, Cmd.none

        | SettingsMsg smsg ->
            let upd, cmd = Settings.update smsg model.Settings
            let mappedCmd = Cmd.map SettingsMsg cmd

            match smsg with
            | Settings.Msg.Save ->
                let saved =
                    { upd with
                        ResultOptions = Some model.Search.ResultOptions
                        FileOutput = Some model.Search.FileOutput }

                { model with Settings = saved }, Cmd.batch [ mappedCmd; Settings.save saved |> Cmd.map SettingsMsg ]

            | Settings.Msg.Loaded s ->
                { model with
                    Settings = upd
                    Search =
                        { model.Search with
                            ResultOptions =
                                match s.ResultOptions with
                                | None -> ResultOptions.initModel
                                | Some ro -> ro
                            FileOutput =
                                match s.FileOutput with
                                | None -> FileOutput.init ()
                                | Some fo -> fo } },
                mappedCmd

            | _ -> { model with Settings = upd }, mappedCmd

    let private view model =
        (TabControl() {
            TabItem("🕝 Recent", View.map RecentMsg (ConfigFile.view model.Recent))

            TabItem(
                "🔍 Search",
                View.map SearchMsg (OutputCommandView.render model.Search model.Settings.ShowThumbnails)
            )
                .reference (searchTab)

            TabItem("🗃 Storage", View.map CacheMsg (Cache.view model.Cache))
            TabItem("⚙ Settings", View.map SettingsMsg (Settings.view model.Settings))
        })
            .margin(10) // to allow dragging the Window while using extendClientAreaToDecorationsHint
            .onAttachedToVisualTree (AttachedToVisualTreeChanged)
#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model =
        let window =
            Window(view model)
                .icon("avares://Ui/SubTubular.ico")
                .title("SubTubular")
                .extendClientAreaToDecorationsHint(true)
                .background (ThemeAware.With(Colors.BlanchedAlmond, Colors.MidnightBlue))

        DesktopApplication(window)
            .requestedThemeVariant(Settings.getThemeVariant (model.Settings.ThemeVariantKey))
#if DEBUG
            .attachDevTools ()
#endif
#endif

    let create () =
        let theme () =
            StyleInclude(baseUri = null, Source = Uri("avares://Ui/Styles.xaml"))

        let program =
            Program.statefulWithCmd init update
            |> Program.withTrace (fun (format, args) -> System.Diagnostics.Debug.WriteLine(format, box args))
            //|> Program.withLogger (fun (logger, program) -> program)
            |> Program.withExceptionHandler (fun ex ->
                let notifyError title message = Notify.error title message |> ignore
#if DEBUG
                printfn $"Exception: %s{ex.ToString()}"
                notifyError "An error occurred" (ex.ToString())
#else
                let logWriting =
                    Threading.Tasks.Task.Run<string * string>(fun () ->
                        task {
                            let! res = ErrorLog.WriteAsync(ex.ToString())
                            return res.ToTuple()
                        })

                let path, report = logWriting.Result

                if path = null then
                    notifyError "The following errors occurred and we were unable to write a log for them:" report
                else
                    notifyError "Errors occured and were logged" ("to " + path)
#endif

                true // handled, try continuing to run
            )
            |> Program.withView app

        FabulousAppBuilder.Configure(theme, program)
