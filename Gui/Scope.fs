namespace SubTubular.Gui

open System
open Fabulous
open Fabulous.Avalonia
open SubTubular
open ScopeDiscriminators
open type Fabulous.Avalonia.View

module Scope =
    type Model =
        { Scope: CommandScope
          ThrottledProgressChanged: ThrottledEvent
          ScopeSearch: ScopeSearch.Model
          Notifications: ScopeNotifications.Model
          ShowSettings: bool }

    type Msg =
        | ToggleSettings of bool
        | SkipChanged of float option
        | TakeChanged of float option
        | CacheHoursChanged of float option
        | Remove
        | RemoveVideo of string
        | ProgressChanged
        | Notified of CommandScope.Notification
        | ProgressValueChanged of float
        | ScopeSearchMsg of ScopeSearch.Msg
        | Common of CommonMsg

    type Intent =
        | RemoveMe
        | DoNothing

    let isForVideos model =
        match model.Scope with
        | :? VideosScope as _ -> true
        | _ -> false

    let private init (scope: CommandScope) focused =
        let progressChanged = ThrottledEvent(TimeSpan.FromMilliseconds(300))
        scope.ProgressChanged.Add(fun args -> progressChanged.Trigger(scope, args))

        { Scope = scope
          ThrottledProgressChanged = progressChanged
          ScopeSearch = ScopeSearch.init scope focused
          Notifications = ScopeNotifications.initModel
          ShowSettings = false }

    /// Re-creates a scope from a reloaded recent command that was previously executed and kicks off its validation,
    /// returning the new scope model and a Cmd for the running validation
    let recreateRecent (scope: CommandScope) =
        let model = init scope false
        let updated, sscmd = ScopeSearch.validate model.ScopeSearch
        { model with ScopeSearch = updated }, sscmd |> Cmd.map ScopeSearchMsg

    /// creates an empty Focused scope of the given type on user command
    let add scopeType =
        let aliases = ""
        let skip = uint16 0
        let take = uint16 50
        let cacheHours = float32 24

        let scope =
            match scopeType with
            | IsChannel -> ChannelScope(aliases, skip, take, cacheHours) :> CommandScope
            | IsPlaylist -> PlaylistScope(aliases, skip, take, cacheHours)
            | IsVideos -> VideosScope(Collections.Generic.List<string>())

        init scope true

    let update msg model =
        match msg with
        | ToggleSettings show -> { model with ShowSettings = show }, Cmd.none, DoNothing

        | SkipChanged skip ->
            match model.Scope with
            | PlaylistLike scope -> scope.Skip <- skip |> Option.defaultValue 0 |> uint16
            | _ -> ()

            model, Cmd.none, DoNothing

        | TakeChanged take ->
            match model.Scope with
            | PlaylistLike scope -> scope.Take <- take |> Option.defaultValue 50 |> uint16
            | _ -> ()

            model, Cmd.none, DoNothing

        | CacheHoursChanged hours ->
            match model.Scope with
            | PlaylistLike scope -> scope.CacheHours <- hours |> Option.defaultValue 24 |> float32
            | _ -> ()

            model, Cmd.none, DoNothing

        | RemoveVideo id ->
            match model.Scope with
            | Vids scope -> scope.Remove(id)
            | _ -> ()

            model, Cmd.none, DoNothing

        | Remove -> model, Cmd.none, RemoveMe

        (*  no need to record passed notification;
            CaptionTrack state notifications are generated selectively based on progress.
            Regular Notifications get rendered from model.Scope via notificationToggle *)
        | Notified _ ->
            { model with
                Notifications = ScopeNotifications.update model.Notifications model.Scope None },
            Cmd.none,
            DoNothing

        | ProgressChanged ->
            let model =
                if ScopeNotifications.needsCaptionTracksUpdate model.Scope.Progress.State then
                    { model with
                        Notifications = ScopeNotifications.updateCaptionTracks model.Notifications model.Scope }
                else
                    model

            model, Cmd.none, DoNothing

        | ProgressValueChanged _ -> model, Cmd.none, DoNothing

        | ScopeSearchMsg ssmsg ->
            let updated, cmd = ScopeSearch.update ssmsg model.ScopeSearch
            { model with ScopeSearch = updated }, cmd |> Cmd.map ScopeSearchMsg, DoNothing

        | Common _ -> model, Cmd.none, DoNothing

    let private validated thumbnailUrl navigateUrl title channel (scope: CommandScope) progress videoId showThumbnails =
        (Grid(coldefs = [ Auto; Auto; Auto; Auto ], rowdefs = [ Auto; Auto ]) {
            match videoId with
            | Some videoId -> Button("❌", RemoveVideo videoId).tooltip("remove this video").gridRowSpan (2)
            | None -> ()

            AsyncImage(thumbnailUrl).maxHeight(30).gridColumn(1).gridRowSpan(2).isVisible (showThumbnails)

            TextBlock(ScopeViews.getIcon (scope.GetType()) + title)
                .tappable(OpenUrl navigateUrl |> Common, "tap to open in the browser")
                .gridColumn(2)
                .gridColumnSpan (2)

            match channel with
            | Some channel -> ScopeViews.channelInfo(channel).gridRow(1).gridColumn (2)
            | None -> ()

            match progress with
            | Some progress -> ScopeViews.progressText(progress).gridRow(1).gridColumn (3)
            | None -> ()
        })
            .columnSpacing (5)

    let private removeBtn () =
        Button("❌", Remove).tooltip ("remove this scope")

    let private notificationToggle model =
        (ScopeNotifications.toggle model.Notifications)
            .onScopeNotified(model.Scope, Notified) // just to trigger re-render of this view
            .tappable (ToggleFlyout >> Common, "some things came up while working on this scope")

    let view model maxWidth showThumbnails =
        VStack() {
            match model.Scope with
            | PlaylistLike playlistLike ->
                HStack(5) {
                    removeBtn ()

                    if playlistLike.IsValid then
                        let validationResult = playlistLike.SingleValidated
                        let playlist = validationResult.Playlist

                        let channel =
                            match playlistLike with
                            | :? PlaylistScope -> Some playlist.Channel
                            | _ -> None

                        validated
                            playlist.ThumbnailUrl
                            validationResult.Url
                            playlist.Title
                            channel
                            playlistLike
                            (Some(model.Scope.Progress.ToString()))
                            None
                            showThumbnails

                        notificationToggle model
                    else
                        ScopeSearch.input model.ScopeSearch showThumbnails |> View.map ScopeSearchMsg

                    ToggleButton("⚙", model.ShowSettings, ToggleSettings).tooltip ("toggle settings")

                    (HStack(5) {
                        Label "skip"
                        uint16UpDown (float playlistLike.Skip) SkipChanged "number of videos to skip"
                        Label "take"
                        uint16UpDown (float playlistLike.Take) TakeChanged "number of videos to search"
                        Label "videos"
                    })
                        .centerHorizontal()
                        .isVisible (model.ShowSettings)

                    (HStack(5) {
                        let tooltip =
                            "The info which videos are in a playlist or channel is cached locally to speed up future searches."
                            + " This controls after how many hours such a cache is considered stale."
                            + " You may want to adjust this depending on how often new videos are added to the playlist or uploaded to the channel."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Note that this doesn't figure into the staleness of video data caches."
                            + " Those are not expected to change often (if ever) and are stored and considered fresh until you explicitly clear them."

                        Label "and look for new ones after"
                        uint16UpDown (float playlistLike.CacheHours) CacheHoursChanged tooltip
                        Label "hours"
                    })
                        .centerHorizontal()
                        .isVisible (model.ShowSettings)
                }

            | Vids videos ->
                let remoteValidated = videos.GetRemoteValidated() |> List.ofSeq

                Grid(coldefs = [ Auto; Auto ], rowdefs = [ Auto; Auto ]) {
                    if remoteValidated.IsEmpty then
                        removeBtn().gridRow (1)
                    else
                        notificationToggle model

                        (HWrap() {
                            for validationResult in remoteValidated do
                                let video = validationResult.Video

                                validated
                                    video.Thumbnail
                                    validationResult.Url
                                    video.Title
                                    (Some video.Channel)
                                    model.Scope
                                    None
                                    (Some video.Id)
                                    showThumbnails
                        })
                            .gridColumn(1)
                            (*   *)
                            .maxWidth (maxWidth)

                    (ScopeSearch.input model.ScopeSearch showThumbnails |> View.map ScopeSearchMsg)
                        .maxWidth(maxWidth)
                        .gridColumn(1)
                        .gridRow (1)
                }

            ScopeSearch.validationErrors model.ScopeSearch

            ProgressBar(0, model.Scope.Progress.AllJobs, model.Scope.Progress.CompletedJobs, ProgressValueChanged)
                .isIndeterminate(model.ScopeSearch.AliasSearch.IsRunning())
                .onThrottledEvent (model.ThrottledProgressChanged, ProgressChanged)
        }
