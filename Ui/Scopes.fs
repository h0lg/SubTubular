namespace Ui

open System
open Fabulous.Avalonia
open SubTubular
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

    type Model = { List: Scope list }

    type Msg =
        | AddScope of Type
        | Focused of Scope
        | RemoveScope of Scope
        | AliasesUpdated of Scope * string
        | ToggleSettings of Scope * bool
        | TopChanged of Scope * float option
        | CacheHoursChanged of Scope * float option
        | ProgressChanged of float

    let private createScope scopeType aliases added =
        let isVideos = scopeType = Type.videos
        let top = if isVideos then None else Some(float 50)
        let cacheHours = if isVideos then None else Some(float 24)

        { Type = scopeType
          Aliases = aliases
          Top = top
          CacheHours = cacheHours
          ShowSettings = false
          Progress = None
          Added = added }

    let initModel = { List = [] }

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

    let updateSearchProgress (progress: BatchProgress) model =
        let scopes =
            model.List
            |> List.map (fun s ->
                let equals (scope: Scope) (commandScope: CommandScope) =
                    match commandScope with
                    | :? ChannelScope as channel -> channel.Alias = scope.Aliases
                    | :? PlaylistScope as playlist -> playlist.Alias = scope.Aliases
                    | :? VideosScope as videos -> videos.Videos = scope.Aliases.Split [| ' ' |]
                    | _ -> failwith $"unsupported {nameof CommandScope} type on {commandScope}"

                let scopeProgress =
                    progress.VideoLists
                    |> Seq.tryFind (fun pair -> equals s pair.Key)
                    |> Option.map (fun pair -> pair.Value)

                if scopeProgress.IsSome then
                    { s with Progress = scopeProgress }
                else
                    s)

        { model with List = scopes }

    let private displayType =
        function
        | Type.videos -> "📼 videos"
        | Type.playlist -> "▶️ playlist"
        | Type.channel -> "📺 channel"
        | _ -> failwith "unknown scope"

    let view model =
        (VStack() {
            HWrap() {
                Label "in"

                for scope in model.List do
                    VStack(5) {
                        HStack(5) {
                            Label(displayType scope.Type)

                            let aliases =
                                TextBox(scope.Aliases, (fun value -> AliasesUpdated(scope, value)))
                                    .watermark (
                                        "by "
                                        + (if scope.Type = Type.videos then
                                               "space-separated IDs or URLs"
                                           elif scope.Type = Type.playlist then
                                               "ID or URL"
                                           else
                                               "handle, slug, user name, ID or URL")
                                    )

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

                            if scope.Type <> Type.videos then
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

                let hasVideosScope =
                    model.List |> List.exists (fun scope -> scope.Type = Type.videos)

                let allScopes = Enum.GetValues<Type>()

                let addable =
                    if hasVideosScope then
                        allScopes |> Array.except [ Type.videos ]
                    else
                        allScopes

                for scopeType in addable do
                    Button(displayType scopeType, AddScope scopeType)
            }
        })
