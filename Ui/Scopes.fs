namespace Ui

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Fabulous.Avalonia
open SubTubular
open YoutubeExplode.Videos
open type Fabulous.Avalonia.View

module Scopes =
    type Type =
        | videos = 0
        | playlist = 1
        | channel = 2

    type Scope =
        { Type: Type
          Aliases: string

          ShowSettings: bool
          Top: float option
          CacheHours: float option
          Progress: BatchProgress.VideoList option
          Added: bool }

    type Model = { List: Scope list; Youtube: Youtube }

    type Msg =
        | AddScope of Type
        | Focused of Scope
        | RemoveScope of Scope
        | AliasesUpdated of Scope * string
        | AliasesSelected of Scope * SelectionChangedEventArgs
        | ToggleSettings of Scope * bool
        | TopChanged of Scope * float option
        | CacheHoursChanged of Scope * float option
        | ProgressChanged of float

    let private isForVideos scope = scope.Type = Type.videos

    let private createScope scopeType aliases added =
        let forVideos = scopeType = Type.videos
        let top = if forVideos then None else Some(float 50)
        let cacheHours = if forVideos then None else Some(float 24)

        { Type = scopeType
          Aliases = aliases
          Top = top
          CacheHours = cacheHours
          ShowSettings = false
          Progress = None
          Added = added }

    let init youtube = { List = []; Youtube = youtube }

    let update msg model =
        match msg with
        | AddScope scope ->
            { model with
                List = model.List @ [ createScope scope "" true ] }

        | Focused scope ->
            let scopes =
                model.List
                |> List.map (fun s -> if s = scope then { s with Added = false } else s)

            { model with List = scopes }

        | RemoveScope scope ->
            { model with
                List = model.List |> List.except [ scope ] }

        | ToggleSettings(scope, show) ->
            let scopes =
                model.List
                |> List.map (fun s -> if s = scope then { s with ShowSettings = show } else s)

            { model with List = scopes }

        | AliasesSelected(scope, args) ->
            if args.AddedItems.Count > 0 then
                let item = args.AddedItems.Item 0 :?> YoutubeSearchResult

                let scopes =
                    model.List
                    |> List.map (fun s -> if s = scope then { s with Aliases = item.Id } else s)

                { model with List = scopes }
            else
                model

        | AliasesUpdated(scope, aliases) ->
            let scopes =
                model.List
                |> List.map (fun s -> if s = scope then { s with Aliases = aliases } else s)

            { model with List = scopes }

        | TopChanged(scope, top) ->
            let scopes =
                model.List |> List.map (fun s -> if s = scope then { s with Top = top } else s)

            { model with List = scopes }

        | CacheHoursChanged(scope, hours) ->
            let scopes =
                model.List
                |> List.map (fun s -> if s = scope then { s with CacheHours = hours } else s)

            { model with List = scopes }

        | ProgressChanged _value -> model

    // takes a batch of progresses and applies them to the model
    let updateSearchProgress (progresses: BatchProgress list) model =
        let matches (scope: Scope) (commandScope: CommandScope) =
            match commandScope with
            | :? ChannelScope as channel -> channel.Alias = scope.Aliases
            | :? PlaylistScope as playlist -> playlist.Alias = scope.Aliases
            | :? VideosScope as videos -> videos.Videos = scope.Aliases.Split [| ' ' |]
            | _ -> failwith $"unsupported {nameof CommandScope} type on {commandScope}"

        let videoLists = progresses |> List.collect (fun p -> p.VideoLists |> List.ofSeq)

        let scopes =
            model.List
            |> List.map (fun scope ->
                let matching = videoLists |> List.filter (fun pair -> matches scope pair.Key)

                match matching with
                | [] -> scope // no match, return unaltered scope
                | _ ->
                    // only apply most completed progress; batch may contain stale reports due to batched throttling
                    let scopeProgress = matching |> List.maxBy (fun pair -> pair.Value.CompletedJobs)

                    { scope with
                        Progress = Some scopeProgress.Value })

        { model with List = scopes }

    let private getAliasWatermark scope =
        match scope.Type with
        | Type.videos -> "comma-separated IDs or URLs"
        | Type.playlist -> "ID or URL"
        | Type.channel -> "handle, slug, user name, ID or URL"
        | _ -> failwith "unmatched scope type " + scope.Type.ToString()

    let private getVideos withValue (enteredText: string) =
        enteredText.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.filter (fun vid ->
            let colon = vid.IndexOf ':'
            let parsable = if colon = -1 then vid else vid.Substring(colon + 1).Trim()
            VideoId.TryParse(parsable).HasValue = withValue)
        |> List.ofArray

    let private selectAliases enteredText (selected: YoutubeSearchResult) multipleCommaSeparated =
        let newText = $"{selected.Title} : {selected.Id}"

        if multipleCommaSeparated then
            let comma = " , "
            getVideos true enteredText @ [ newText + comma ] |> String.concat comma
        else
            newText

    let private searchAliasesAsync model scope (text: string) (cancellation: CancellationToken) : Task<seq<obj>> =
        Async.StartAsTask(
            async {
                match scope.Type with
                | Type.channel ->
                    let! channels = model.Youtube.SearchForChannelsAsync(text, cancellation) |> Async.AwaitTask
                    return channels |> Seq.map (fun c -> c)
                | Type.playlist ->
                    let! playlists = model.Youtube.SearchForPlaylistsAsync(text, cancellation) |> Async.AwaitTask
                    return playlists |> Seq.map (fun c -> c)
                | Type.videos ->
                    let searchTerm = getVideos false text |> List.head
                    let! videos = model.Youtube.SearchForVideosAsync(searchTerm, cancellation) |> Async.AwaitTask
                    return videos |> Seq.map (fun c -> c)
                | _ ->
                    //failwith "unknown scope type"
                    return []
            },
            TaskCreationOptions.None,
            cancellation
        )

    let private displayType =
        function
        | Type.videos -> "📼 videos"
        | Type.playlist -> "▶️ playlist"
        | Type.channel -> "📺 channel"
        | _ -> failwith "unknown scope"

    let private getAddableTypes model =
        let allTypes = Enum.GetValues<Type>()

        if model.List |> List.exists isForVideos then
            allTypes |> Array.except [ Type.videos ]
        else
            allTypes

    let view model =
        (VStack() {
            HWrap() {
                Label "in"

                for scope in model.List do
                    let forVideos = isForVideos scope

                    VStack(5) {
                        HStack(5) {
                            Label(displayType scope.Type)
                            Label(scope.Aliases)

                            let aliases =
                                AutoCompleteBox(
                                    scope.Aliases,
                                    (fun value -> AliasesUpdated(scope, value)),
                                    fun text ct -> searchAliasesAsync model scope text ct
                                )
                                    .minimumPrefixLength(3)
                                    .itemSelector(fun enteredText item ->
                                        selectAliases enteredText (item :?> YoutubeSearchResult) forVideos)
                                    .onSelectionChanged(fun args -> AliasesSelected(scope, args))
                                    .filterMode(AutoCompleteFilterMode.None)
                                    .watermark ("by " + getAliasWatermark scope)

                            if scope.Added then
                                aliases
                                    .focus(true) // to enable typing immediately
                                    .onGotFocus (fun args -> Focused scope) // to clear Added flag
                            else
                                aliases

                            (HStack(5) {
                                Label "search top"

                                NumericUpDown(
                                    0,
                                    float UInt16.MaxValue,
                                    scope.Top,
                                    fun value -> TopChanged(scope, value)
                                )
                                    .formatString("F0")
                                    .tip (ToolTip("number of videos to search"))

                                Label "videos"
                            })
                                .centerHorizontal()
                                .isVisible (scope.ShowSettings)

                            (HStack(5) {
                                Label "and look for new ones after"

                                NumericUpDown(
                                    0,
                                    float UInt16.MaxValue,
                                    scope.CacheHours,
                                    fun value -> CacheHoursChanged(scope, value)
                                )
                                    .formatString("F0")
                                    .tip (
                                        ToolTip(
                                            "The info about which videos are in a playlist or channel is cached locally to speed up future searches."
                                            + " This controls after how many hours such a cache is considered stale."
                                            + Environment.NewLine
                                            + Environment.NewLine
                                            + "Note that this doesn't concern the video data caches,"
                                            + " which are not expected to change often and are stored until you explicitly clear them."
                                        )
                                    )

                                Label "hours"
                            })
                                .centerHorizontal()
                                .isVisible (scope.ShowSettings)

                            if not forVideos then
                                ToggleButton("⚙", scope.ShowSettings, (fun show -> ToggleSettings(scope, show)))
                                    .tip (ToolTip("toggle settings"))

                            Button("❌", RemoveScope scope).tip (ToolTip("remove this scope"))
                        }

                        if scope.Progress.IsSome then
                            ProgressBar(
                                0,
                                scope.Progress.Value.AllJobs,
                                scope.Progress.Value.CompletedJobs,
                                ProgressChanged
                            )
                                .progressTextFormat(scope.Progress.Value.ToString())
                                .showProgressText (true)
                    }
            }

            HStack(5) {
                Label "add"

                for scopeType in getAddableTypes model do
                    Button(displayType scopeType, AddScope scopeType)
            }
        })
