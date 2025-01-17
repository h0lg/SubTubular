namespace SubTubular.Gui

open System
open System.Linq
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module Scope =
    let private (|IsChannel|IsPlaylist|IsVideos|) (t: Type) =
        match t with
        | _ when t = typeof<ChannelScope> -> IsChannel
        | _ when t = typeof<PlaylistScope> -> IsPlaylist
        | _ when t = typeof<VideosScope> -> IsVideos
        | _ -> failwith ("unknown scope type " + t.FullName)

    let private (|Channel|Playlist|Videos|) (scope: CommandScope) =
        match scope with
        | :? ChannelScope as channel -> Channel channel
        | :? PlaylistScope as playlist -> Playlist playlist
        | :? VideosScope as videos -> Videos videos
        | _ -> failwith $"unsupported {nameof CommandScope} type on {scope}"

    let private (|PlaylistLike|Vids|) (scope: CommandScope) =
        match scope with
        | :? PlaylistLikeScope as playlist -> PlaylistLike playlist
        | :? VideosScope as videos -> Vids videos
        | _ -> failwith $"unsupported {nameof CommandScope} type on {scope}"

    module Alias =
        /// <summary>Removes title prefix applied by <see cref="label" />, i.e. everything before the last ':'</summary>
        let clean (alias: string) =
            let colon = alias.LastIndexOf(':')

            if colon < 0 then
                alias
            else
                alias.Substring(colon + 1).Trim()

        /// <summary>Prefixes the <paramref name="alias" /> with the <paramref name="title" />.
        /// Extract the alias from the result using <see cref="clean" />.</summary>
        let label (title: string) alias =
            let cleanTitle = title.Replace(":", "")
            $"{cleanTitle} : {alias}"

    module VideosInput =
        let join values =
            values
            |> Seq.filter (fun (s: string) -> s.IsNonWhiteSpace())
            |> String.concat "\n"

        let private split (input: string) =
            input.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.map _.Trim()

        let splitAndClean (input: string) =
            let array = split input |> Array.map Alias.clean
            array.ToList()

        /// splits and partitions the input into two lists:
        /// first the remote-validated Video IDs (whether they are in the input or not),
        /// second the inputs that haven't already been remote-validated and are considered search terms
        let partition (input: string) (scope: VideosScope) =
            let remoteValidated = scope.GetRemoteValidated().Ids() |> List.ofSeq

            let searchTerms =
                split input
                |> List.ofArray
                // exclude remote-validated
                |> List.filter (fun phrase -> phrase |> Alias.clean |> remoteValidated.Contains |> not)

            remoteValidated, searchTerms

    type AliasSearch(scope: CommandScope) =
        let mutable searching: CancellationTokenSource = null
        let mutable selectedText: string = null
        let mutable isRemoteValidating: bool = false
        let input = ViewRef<AutoCompleteBox>()

        let isRunning () = searching <> null

        let cancel () =
            if isRunning () then
                searching.Cancel()
                searching.Dispose()
                searching <- null

        let yieldResults (results: YoutubeSearchResult seq) =
            if isRunning () && not searching.Token.IsCancellationRequested then
                searching.Dispose()
                searching <- null
                results |> Seq.cast<obj>
            else
                Seq.empty

        member this.Input = input
        member this.IsRunning = isRunning
        member this.Cancel = cancel

        (*  workaround using a mutable property on a type instance
            because a field on the immutable model ref doesn't always represent the correct state
            for some reason at the time of writing *)
        member this.IsRemoteValidating
            with get () = isRemoteValidating
            and set (value) = isRemoteValidating <- value

        // called when either using arrow keys to cycle through results in dropdown or mouse to click one
        member this.SelectAliases text (item: obj) =
            let result = item :?> YoutubeSearchResult

            match scope with
            | Vids vids ->
                let _, searchTerms = VideosInput.partition text vids
                let labeledId = Alias.label result.Title result.Id
                selectedText <- labeledId :: searchTerms |> VideosInput.join
            // replace search term for playlist-like scopes
            | PlaylistLike _ -> selectedText <- Alias.label result.Title result.Id

            selectedText

        member this.SearchAsync (text: string) (cancellation: CancellationToken) : Task<obj seq> =
            task {
                // only start search if input has keyboard focus & avoid re-triggering it for the same search term after selection
                if input.Value.IsKeyboardFocusWithin && text <> selectedText then
                    cancellation.Register(cancel) |> ignore // register cancellation of running search when outer cancellation is requested
                    cancel () // cancel any older running search
                    searching <- new CancellationTokenSource() // and create a new source for this one

                    try
                        match scope with
                        | Channel _ ->
                            let! channels = Services.Youtube.SearchForChannelsAsync(text, searching.Token)
                            return yieldResults channels
                        | Playlist _ ->
                            let! playlists = Services.Youtube.SearchForPlaylistsAsync(text, searching.Token)
                            return yieldResults playlists
                        | Videos vids ->
                            let remoteValidated, searchTerms = VideosInput.partition text vids

                            match searchTerms with
                            | [] -> return []
                            | _ ->
                                let! videos =
                                    // OR-combine, see https://seosly.com/blog/youtube-search-operators/#Pipe
                                    Services.Youtube.SearchForVideosAsync(
                                        searchTerms |> String.concat " | ",
                                        searching.Token
                                    )

                                let alreadyAdded = (vids.Videos |> List.ofSeq) @ remoteValidated

                                return
                                    videos
                                    // exclude already added or selected videos from results
                                    |> Seq.filter (fun v -> alreadyAdded.Contains v.Id |> not)
                                    |> yieldResults

                    // drop exception caused by outside cancellation
                    with :? OperationCanceledException when cancellation.IsCancellationRequested ->
                        return []
                else
                    return []
            }

    type Model =
        { Scope: CommandScope
          CaptionStatusNotifications: CommandScope.Notification list
          Aliases: string
          AliasSearch: AliasSearch
          AliasSearchDropdownOpen: bool
          ValidationError: string
          ShowSettings: bool
          Added: bool }

    type Msg =
        | AliasesUpdated of string
        | AliasesLostFocus of RoutedEventArgs
        | AliasesSearchDropdownToggled of bool
        | ValidationSucceeded
        | ValidationFailed of exn
        | ToggleSettings of bool
        | SkipChanged of float option
        | TakeChanged of float option
        | CacheHoursChanged of float option
        | Remove
        | RemoveVideo of string
        | ProgressChanged
        | Notified of CommandScope.Notification
        | ProgressValueChanged of float
        | Common of CommonMsg

    type Intent =
        | RemoveMe
        | DoNothing

    let isForVideos model =
        match model.Scope with
        | :? VideosScope as _ -> true
        | _ -> false

    let private remoteValidate model token =
        task {
            try
                match model.Scope with
                | Channel channel ->
                    do! RemoteValidate.ChannelsAsync([| channel |], Services.Youtube, Services.DataStore, token)
                | Playlist playlist -> do! RemoteValidate.PlaylistAsync(playlist, Services.Youtube, token)
                | Videos videos -> do! RemoteValidate.AllVideosAsync(videos, Services.Youtube, token)

                return ValidationSucceeded
            with exn ->
                return ValidationFailed exn
        }

    let private syncScopeWithAliases model =
        let aliases = model.Aliases

        let updatedAliases =
            match model.Scope with
            | PlaylistLike playlist ->
                playlist.Alias <- Alias.clean aliases
                aliases
            | Vids vids ->
                let _, searchTerms = VideosInput.partition aliases vids

                // inputs that pre-validate, but don't remote-validate
                let remoteInvalid = vids.GetRemoteInvalidatedIds().ToArray()

                let preValidating =
                    searchTerms
                    |> List.filter (fun phrase ->
                        let alias = Alias.clean phrase
                        let id = VideosScope.TryParseId(alias)

                        // pre-vadidates and was not already remotely invalidated
                        id <> null && not (remoteInvalid.Contains(id)))

                let missing =
                    preValidating
                    |> List.map Alias.clean
                    |> List.except vids.Videos // already added to inputs
                    |> List.except remoteInvalid // already remotely invalidated

                if not missing.IsEmpty then
                    vids.Videos.AddRange missing // to have them validated

                searchTerms |> VideosInput.join

        { model with Aliases = updatedAliases }

    /// first pre-validates the scope, then triggers remoteValidate on success
    let private validate model =
        if not model.AliasSearch.IsRemoteValidating && model.Scope.RequiresValidation() then
            match Prevalidate.Scope model.Scope with
            | null ->
                if model.Scope.IsPrevalidated then
                    model.AliasSearch.IsRemoteValidating <- true
                    model, remoteValidate model CancellationToken.None |> Cmd.OfTask.msg
                else
                    model, Cmd.none
            | error -> { model with ValidationError = error }, Cmd.none
        else
            model, Cmd.none

    let private init scope added =
        { Scope = scope
          CaptionStatusNotifications = []
          Aliases = // set from scope to sync
            match scope with
            | PlaylistLike pl -> pl.Alias
            | Vids v -> v.Videos |> VideosInput.join
          AliasSearchDropdownOpen = false
          AliasSearch = AliasSearch(scope)
          ValidationError = null
          ShowSettings = false
          Added = added }

    /// Re-creates a scope from a reloaded recent command that was previously executed and kicks off its validation,
    /// returning the new scope model and a Cmd for the running validation
    let recreateRecent (scope: CommandScope) =
        let model = init scope false
        validate model

    /// creates a scope on user command, marking it as Added for it to be focused
    let add scopeType =
        let aliases = ""
        let skip = uint16 0
        let take = uint16 50
        let cacheHours = float32 24

        let scope =
            match scopeType with
            | IsChannel -> ChannelScope(aliases, skip, take, cacheHours) :> CommandScope
            | IsPlaylist -> PlaylistScope(aliases, skip, take, cacheHours)
            | IsVideos -> VideosScope(VideosInput.splitAndClean aliases)

        init scope true

    let private getCaptionTrackDownloadStateNotifications model =
        model.Scope.GetCaptionTrackDownloadStates().Irregular().AsNotifications()
        |> List.ofArray

    let update msg model =
        match msg with
        | ToggleSettings show -> { model with ShowSettings = show }, Cmd.none, DoNothing

        | AliasesUpdated aliases ->
            { model with
                Added = false
                ValidationError = null
                Aliases = aliases },
            Cmd.none,
            DoNothing

        | AliasesLostFocus _ ->
            model.AliasSearch.Cancel() // to avoid population after losing focus
            let model = syncScopeWithAliases model
            let model, cmd = validate model
            model, cmd, DoNothing

        | AliasesSearchDropdownToggled isOpen ->
            let model =
                { model with
                    AliasSearchDropdownOpen = isOpen }

            let model, cmd =
                if isOpen then
                    model, Cmd.none
                else // trigger update & validation on closing (e.g. after click) to lock in selection or display error
                    syncScopeWithAliases model |> validate

            model, cmd, DoNothing

        | ValidationSucceeded ->
            model.AliasSearch.IsRemoteValidating <- false

            { model with
                CaptionStatusNotifications = getCaptionTrackDownloadStateNotifications model
                ValidationError = null }
            |> syncScopeWithAliases, // to remove remote-validated from input
            Cmd.none,
            DoNothing

        | ValidationFailed exn ->
            model.AliasSearch.IsRemoteValidating <- false

            { model with
                ValidationError = exn.GetRootCauses() |> Seq.map (fun ex -> ex.Message) |> String.concat "\n" },
            Cmd.none,
            DoNothing

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

        | ProgressChanged ->
            let model =
                match model.Scope.Progress.State with
                | VideoList.Status.searched ->
                    { model with
                        CaptionStatusNotifications = getCaptionTrackDownloadStateNotifications model }
                | _ -> model

            model, Cmd.none, DoNothing

        | ProgressValueChanged _ -> model, Cmd.none, DoNothing
        | Notified _ -> model, Cmd.none, DoNothing
        | Common _ -> model, Cmd.none, DoNothing

    let private getAliasWatermark model =
        "search YouTube - or enter "
        + match model.Scope with
          | Videos _ -> "IDs or URLs; one per line"
          | Playlist _ -> "ID or URL"
          | Channel _ -> "handle, slug, user name, ID or URL"

    let private channelInfo channel =
        TextBlock(Icon.channel + channel).smallDemoted ()

    let getIcon (t: Type) =
        match t with
        | IsVideos -> Icon.video
        | IsPlaylist -> Icon.playlist
        | IsChannel -> Icon.channel

    let displayType (t: Type) withKeyBinding =
        getIcon t
        + if withKeyBinding then "_" else ""
        + match t with
          | IsVideos -> "videos"
          | IsPlaylist -> "playlist"
          | IsChannel -> "channel"

    let private progressText text = TextBlock(text).right().smallDemoted ()

    let private validated thumbnailUrl navigateUrl title channel (scope: CommandScope) progress videoId showThumbnails =
        Grid(coldefs = [ Auto; Auto; Auto; Auto ], rowdefs = [ Auto; Auto ]) {
            match videoId with
            | Some videoId -> Button("❌", RemoveVideo videoId).tooltip("remove this video").gridRowSpan (2)
            | None -> ()

            AsyncImage(thumbnailUrl)
                .tappable(OpenUrl navigateUrl |> Common, "tap to open in the browser")
                .maxHeight(30)
                .gridColumn(1)
                .gridRowSpan(2)
                .isVisible (showThumbnails)

            TextBlock(getIcon (scope.GetType()) + title).gridColumn(2).gridColumnSpan (2)

            match channel with
            | Some channel -> channelInfo(channel).gridColumn(2).gridRow (1)
            | None -> ()

            match progress with
            | Some progress -> progressText(progress).gridRow(1).gridColumn (3)
            | None -> ()
        }

    let private search model showThumbnails =
        Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Auto ]) {
            Label(displayType (model.Scope.GetType()) false).padding (0)

            let autoComplete =
                AutoCompleteBox(model.AliasSearch.SearchAsync)
                    .minimumPopulateDelay(TimeSpan.FromMilliseconds 300)
                    .onTextChanged(model.Aliases, AliasesUpdated)
                    .onDropDownOpened(model.AliasSearchDropdownOpen, AliasesSearchDropdownToggled)
                    .onLostFocus(AliasesLostFocus)
                    .minimumPrefixLength(3)
                    .filterMode(AutoCompleteFilterMode.None)
                    .focus(model.Added)
                    .watermark(getAliasWatermark model)
                    .itemSelector(model.AliasSearch.SelectAliases)
                    .itemTemplate(fun (result: YoutubeSearchResult) ->
                        HStack(5) {
                            AsyncImage(result.Thumbnail).isVisible (showThumbnails)

                            VStack(5) {
                                TextBlock(result.Title)

                                if result.Channel <> null then
                                    channelInfo (result.Channel)
                            }
                        })
                    .reference(model.AliasSearch.Input)
                    .margin(5, 0, 0, 0)
                    .gridColumn(1)
                    .gridRowSpan (2)

            match model.Scope with
            | Videos videos ->
                progressText(videos.Progress.ToString()).gridRow (1)
                autoComplete.acceptReturn ()
            | _ -> autoComplete
        }

    let private removeBtn () =
        Button("❌", Remove).tooltip ("remove this scope")

    let private notificationFlyout (notifications: CommandScope.Notification list) =
        Flyout(
            ItemsControl(
                notifications,
                fun ntf ->
                    VStack() {
                        TextBlock ntf.Title

                        if ntf.Video <> null then
                            TextBlock ntf.Video.Title

                        if ntf.Message.IsNonEmpty() then
                            TextBlock ntf.Message

                        if ntf.Errors <> null then
                            for err in ntf.Errors do
                                TextBlock err.Message
                    }
            )
        )
            .placement(PlacementMode.BottomEdgeAlignedRight)
            .showMode (FlyoutShowMode.Standard)

    let private notificationToggle model =
        let notifications =
            model.CaptionStatusNotifications @ (List.ofSeq model.Scope.Notifications)

        TextBlock($"⚠️ {notifications.Length}")
            .onScopeNotified(model.Scope, Notified)
            .attachedFlyout(notificationFlyout notifications)
            .tappable(ToggleFlyout >> Common, "some things came up while working on this scope")
            .isVisible (not notifications.IsEmpty)

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
                    else
                        search model showThumbnails

                    notificationToggle model

                    ToggleButton("⚙", model.ShowSettings, ToggleSettings)
                        .tooltip ("toggle settings")

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

                    (search model showThumbnails).maxWidth(maxWidth).gridColumn(1).gridRow (1)
                }

            TextBlock(model.ValidationError)
                .foreground(Colors.Red)
                .wrap()
                // display if there is a validation error and the model state is not valid
                .isVisible (model.ValidationError <> null && not model.Scope.IsValid)

            ProgressBar(0, model.Scope.Progress.AllJobs, model.Scope.Progress.CompletedJobs, ProgressValueChanged)
                .isIndeterminate(model.AliasSearch.IsRunning())
                .onScopeProgressChanged (model.Scope, ProgressChanged)
        }
