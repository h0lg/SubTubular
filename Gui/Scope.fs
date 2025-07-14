﻿namespace SubTubular.Gui

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Media
open Fabulous.Avalonia
open SubTubular
open YoutubeExplode.Videos
open type Fabulous.Avalonia.View

module Scope =
    type Type =
        | videos = 0
        | playlist = 1
        | channel = 2

    type AliasSearch() =
        let mutable searching: CancellationTokenSource = null

        let cancelRunning () =
            if searching <> null then
                searching.Cancel()
                searching.Dispose()

        let getVideos withValue (enteredText: string) =
            enteredText.Split(',', StringSplitOptions.RemoveEmptyEntries)
            |> Array.filter (fun vid ->
                let colon = vid.IndexOf ':'
                let parsable = if colon = -1 then vid else vid.Substring(colon + 1).Trim()
                VideoId.TryParse(parsable).HasValue = withValue)
            |> List.ofArray

        // called when either using arrow keys to cycle through results in dropdown or mouse to click one
        member this.SelectAliases enteredText (selected: YoutubeSearchResult) multipleCommaSeparated =
            let newText = $"{selected.Title} : {selected.Id}"

            if multipleCommaSeparated then
                let comma = " , "
                getVideos true enteredText @ [ newText + comma ] |> String.concat comma
            else
                newText

        member this.SearchAsync
            (youtube: Youtube)
            scopeType
            (text: string)
            (cancellation: CancellationToken)
            : Task<obj seq> =
            task {
                cancellation.Register(fun () ->
                    cancelRunning ()
                    searching <- null)
                |> ignore

                cancelRunning ()
                searching <- new CancellationTokenSource()

                let yieldResults (results: YoutubeSearchResult seq) =
                    if searching = null || searching.Token.IsCancellationRequested then
                        Seq.empty
                    else
                        results |> Seq.cast<obj>

                match scopeType with
                | Type.channel ->
                    let! channels = youtube.SearchForChannelsAsync(text, searching.Token)
                    return yieldResults channels
                | Type.playlist ->
                    let! playlists = youtube.SearchForPlaylistsAsync(text, searching.Token)
                    return yieldResults playlists
                | Type.videos ->
                    match getVideos false text |> List.tryHead with
                    | Some searchTerm ->
                        let! videos = youtube.SearchForVideosAsync(searchTerm, searching.Token)
                        return yieldResults videos
                    | None -> return []
                | _ -> return []
            }

    type Model =
        { Type: Type
          Aliases: string
          AliasSearch: AliasSearch

          ShowSettings: bool
          Top: float option
          CacheHours: float option
          Progress: BatchProgress.VideoList option
          Added: bool
          Youtube: Youtube }

    type Msg =
        | AliasesUpdated of string
        | AliasesSelected of SelectionChangedEventArgs
        | ToggleSettings of bool
        | TopChanged of float option
        | CacheHoursChanged of float option
        | Remove
        | ProgressChanged of float

    type Intent =
        | RemoveMe
        | DoNothing

    let isForVideos model = model.Type = Type.videos

    let private create scopeType aliases youtube top cacheHours added =
        { Type = scopeType
          Aliases = aliases
          AliasSearch = AliasSearch()
          Top = top
          CacheHours = cacheHours
          ShowSettings = false
          Progress = None
          Added = added
          Youtube = youtube }

    let init scopeType aliases youtube top cacheHours =
        create scopeType aliases youtube top cacheHours false

    let add scopeType youtube =
        let forVideos = scopeType = Type.videos
        let top = if forVideos then None else Some(float 50)
        let cacheHours = if forVideos then None else Some(float 24)
        create scopeType "" youtube top cacheHours true

    let update msg model =
        match msg with
        | ToggleSettings show -> { model with ShowSettings = show }, DoNothing

        | AliasesSelected args ->
            (if args.AddedItems.Count > 0 then
                 let item = args.AddedItems.Item 0 :?> YoutubeSearchResult

                 { model with Aliases = item.Id }
             else
                 model),
            DoNothing

        | AliasesUpdated aliases ->
            { model with
                Added = false
                Aliases = aliases },
            DoNothing

        | TopChanged top -> { model with Top = top }, DoNothing
        | CacheHoursChanged hours -> { model with CacheHours = hours }, DoNothing
        | Remove -> model, RemoveMe
        | ProgressChanged _ -> model, DoNothing

    let private getAliasWatermark scope =
        match scope.Type with
        | Type.videos -> "comma-separated IDs or URLs"
        | Type.playlist -> "ID or URL"
        | Type.channel -> "handle, slug, user name, ID or URL"
        | _ -> failwith "unmatched scope type " + scope.Type.ToString()

    // removes title prefix, i.e. everything before the last ':'
    let cleanAlias (alias: string) =
        let idx = alias.LastIndexOf(':')
        if idx < 0 then alias else alias.Substring(idx + 1).Trim()

    let matches model (commandScope: CommandScope) =
        match commandScope with
        | :? ChannelScope as channel -> channel.Alias = cleanAlias model.Aliases
        | :? PlaylistScope as playlist -> playlist.Alias = cleanAlias model.Aliases
        | :? VideosScope as videos -> videos.Videos = (model.Aliases.Split [| ',' |] |> Array.map cleanAlias)
        | _ -> failwith $"unsupported {nameof CommandScope} type on {commandScope}"

    let displayType =
        function
        | Type.videos -> "📼 videos"
        | Type.playlist -> "▶️ playlist"
        | Type.channel -> "📺 channel"
        | _ -> failwith "unknown scope"

    let view model =
        let forVideos = isForVideos model

        VStack(5) {
            HStack(5) {
                Button("❌", Remove).tip (ToolTip("remove this scope"))
                Label(displayType model.Type)

                AutoCompleteBox(fun text ct -> model.AliasSearch.SearchAsync model.Youtube model.Type text ct)
                    .minimumPopulateDelay(TimeSpan.FromMilliseconds 300)
                    .onTextChanged(model.Aliases, AliasesUpdated)
                    .minimumPrefixLength(3)
                    .onSelectionChanged(AliasesSelected)
                    .filterMode(AutoCompleteFilterMode.None)
                    .focus(model.Added)
                    .multiline(true)
                    .watermark("by " + getAliasWatermark model)
                    .itemSelector(fun enteredText item ->
                        model.AliasSearch.SelectAliases enteredText (item :?> YoutubeSearchResult) forVideos)
                    .itemTemplate (fun (result: YoutubeSearchResult) ->
                        HStack(5) {
                            AsyncImage(result.Thumbnail)
                            TextBlock(result.Title)

                            if result.Channel <> null then
                                TextBlock(result.Channel).foreground (Colors.Gray)
                        })

                if not forVideos then
                    ToggleButton("⚙", model.ShowSettings, ToggleSettings)
                        .tip (ToolTip("toggle settings"))

                (HStack(5) {
                    Label "search top"

                    NumericUpDown(0, float UInt16.MaxValue, model.Top, TopChanged)
                        .formatString("F0")
                        .tip (ToolTip("number of videos to search"))

                    Label "videos"
                })
                    .centerHorizontal()
                    .isVisible (model.ShowSettings)

                (HStack(5) {
                    Label "and look for new ones after"

                    NumericUpDown(0, float UInt16.MaxValue, model.CacheHours, CacheHoursChanged)
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
                    .isVisible (model.ShowSettings)
            }

            if model.Progress.IsSome then
                ProgressBar(0, model.Progress.Value.AllJobs, model.Progress.Value.CompletedJobs, ProgressChanged)
                    .progressTextFormat(model.Progress.Value.ToString())
                    .showProgressText (true)
        }
