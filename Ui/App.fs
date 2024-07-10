namespace Ui

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
        { Search: Search.Model
          Settings: Settings.Model
          Cache: Cache.Model
          Recent: ConfigFile.Model }

    type Msg =
        | CacheMsg of Cache.Msg
        | RecentMsg of ConfigFile.Msg
        | SettingsMsg of Settings.Msg
        | SearchMsg of Search.Msg

        | AttachedToVisualTreeChanged of VisualTreeAttachmentEventArgs
        | Common of CommonMsg

    let private initModel =
        { Cache = Cache.initModel
          Settings = Settings.initModel
          Recent = ConfigFile.initModel
          Search = Search.initModel }

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
                    let updated, cmd = Search.load cmdClone model.Search
                    { model with Search = updated }, Cmd.map SearchMsg cmd
                | _ -> model, Cmd.none

            let recent, rCmd = ConfigFile.update rmsg model.Recent
            { loaded with Recent = recent }, Cmd.batch [ cmd; Cmd.map RecentMsg rCmd ]

        | AttachedToVisualTreeChanged args ->
            let notifier = FabApplication.Current.WindowNotificationManager
            notifier.Position <- NotificationPosition.BottomRight
            Services.Notifier <- notifier
            model, Cmd.none

        | SearchMsg smsg ->
            let fwdCmd =
                match smsg with
                | Search.Msg.Common msg -> Common msg |> Cmd.ofMsg
                | Search.Msg.CommandValidated cmd -> ConfigFile.CommandRun cmd |> RecentMsg |> Cmd.ofMsg
                | Search.Msg.ResultOptionsChanged -> requestSaveSettings ()
                | Search.Msg.FileOutputMsg fom ->
                    match fom with
                    | FileOutput.Msg.SaveOutput -> Cmd.none
                    | _ -> requestSaveSettings ()
                | _ -> Cmd.none

            let updated, cmd = Search.update smsg model.Search
            { model with Search = updated }, Cmd.batch [ fwdCmd; Cmd.map SearchMsg cmd ]

        | Common cmsg ->
            match cmsg with
            | Notify title -> model, Services.notifyInfo title
            | Fail title -> model, Services.notifyError title ""

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

            TabItem("🔍 Search", View.map SearchMsg (Search.view model.Search))
                .reference (searchTab)

            TabItem("🗃 Storage", View.map CacheMsg (Cache.view model.Cache))
            TabItem("⚙ Settings", View.map SettingsMsg (Settings.view model.Settings))
        })
            .onAttachedToVisualTree (AttachedToVisualTreeChanged)
#if MOBILE
    let app model = SingleViewApplication(view model)
#else
    let app model =
        let window =
            Window(view model)
                .icon("avares://Ui/SubTubular.ico")
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
            StyleInclude(baseUri = null, Source = Uri("avares://Ui/Styles.xaml"))

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
