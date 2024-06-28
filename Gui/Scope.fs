namespace SubTubular.Gui

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Animation
open Avalonia.Controls
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open SubTubular
open YoutubeExplode.Videos
open type Fabulous.Avalonia.View

module Scope =
    type Type =
        | videos = 0
        | playlist = 1
        | channel = 2

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
        let join values = values |> String.concat " , "

        let private split (input: string) =
            input.Split(',', StringSplitOptions.RemoveEmptyEntries)

        let cleanTitle (title: string) = title.Replace(",", "")

        let splitAndClean (input: string) = split input |> Array.map Alias.clean

        /// splits and partitions the input into two lists:
        /// first the pre-validated, labeled/uncleaned video aliases,
        /// second the unvalidated values considered search terms
        let partition (input: string) =
            split input
            |> List.ofArray
            |> List.partition (fun alias ->
                let value = Alias.clean alias
                VideoId.TryParse(value).HasValue)

    type AliasSearch() =
        let mutable searching: CancellationTokenSource = null
        let input = ViewRef<AutoCompleteBox>()
        let heartBeat = ViewRef<Animation>()

        let isRunning () = searching <> null

        let cancel () =
            if isRunning () then
                searching.Cancel()
                searching.Dispose()
                searching <- null

        /// animates the input with the heartBeat until searchToken is cancelled
        let animateInput searchToken =
            heartBeat.Value.IterationCount <- IterationCount.Infinite
            heartBeat.Value.RunAsync(input.Value, searchToken) |> ignore

        let yieldResults (results: YoutubeSearchResult seq) =
            if isRunning () && not searching.Token.IsCancellationRequested then
                cancel () // to stop animation
                results |> Seq.cast<obj>
            else
                cancel () // to stop animation
                Seq.empty

        member this.Input = input
        member this.HeartBeat = heartBeat

        // called when either using arrow keys to cycle through results in dropdown or mouse to click one
        member this.SelectAliases text (result: YoutubeSearchResult) forVideos =
            if forVideos then
                let selection, searchTerms = VideosInput.partition text
                let title = VideosInput.cleanTitle result.Title
                let labeledAlias = Alias.label title result.Id
                selection @ [ labeledAlias ] @ searchTerms |> VideosInput.join
            else
                Alias.label result.Title result.Id

        member this.SearchAsync
            (youtube: Youtube)
            scopeType
            (text: string)
            (cancellation: CancellationToken)
            : Task<obj seq> =
            task {
                if input.Value.IsKeyboardFocusWithin then // only start search if input has keyboard focus
                    cancellation.Register(cancel) |> ignore // register cancellation of running search when outer cancellation is requested
                    cancel () // cancel any older running search
                    searching <- new CancellationTokenSource() // and create a new source for this one
                    animateInput (searching.Token) // pass running search token to stop it when the search completes or is cancelled

                    match scopeType with
                    | Type.channel ->
                        let! channels = youtube.SearchForChannelsAsync(text, searching.Token)
                        return yieldResults channels
                    | Type.playlist ->
                        let! playlists = youtube.SearchForPlaylistsAsync(text, searching.Token)
                        return yieldResults playlists
                    | Type.videos ->
                        match VideosInput.partition text with
                        | _, [] -> return []
                        | _, searchTerms ->
                            let! videos =
                                youtube.SearchForVideosAsync(searchTerms |> String.concat " or ", searching.Token)

                            return yieldResults videos
                    | _ -> return []
                else
                    return []
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

    let matches model (commandScope: CommandScope) =
        match commandScope with
        | :? ChannelScope as channel -> channel.Alias = Alias.clean model.Aliases
        | :? PlaylistScope as playlist -> playlist.Alias = Alias.clean model.Aliases
        | :? VideosScope as videos -> videos.Videos = (VideosInput.splitAndClean model.Aliases)
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
                Button("❌", Remove).tooltip ("remove this scope")
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
                    .itemTemplate(fun (result: YoutubeSearchResult) ->
                        HStack(5) {
                            AsyncImage(result.Thumbnail)
                            TextBlock(result.Title)

                            if result.Channel <> null then
                                TextBlock(result.Channel).foreground (Colors.Gray)
                        })
                    .reference(model.AliasSearch.Input)
                    .animation (
                        // pulses the scale like a heart beat to indicate activity
                        (Animation(TimeSpan.FromSeconds(2.)) {
                            // extend slightly but quickly to get a pulse effect
                            KeyFrame(ScaleTransform.ScaleXProperty, 1.05).cue (0.1)
                            KeyFrame(ScaleTransform.ScaleYProperty, 1.05).cue (0.1)
                            // contract slightly to get a bounce-back effect
                            KeyFrame(ScaleTransform.ScaleXProperty, 0.95).cue (0.15)
                            KeyFrame(ScaleTransform.ScaleYProperty, 0.95).cue (0.15)
                            // return to original size rather quickly
                            KeyFrame(ScaleTransform.ScaleXProperty, 1).cue (0.2)
                            KeyFrame(ScaleTransform.ScaleYProperty, 1).cue (0.2)
                        })
                            .delay(TimeSpan.FromSeconds 1.) // to avoid a "heart attack", i.e. restarting the animation by typing
                            .reference (model.AliasSearch.HeartBeat)
                    )

                if not forVideos then
                    ToggleButton("⚙", model.ShowSettings, ToggleSettings)
                        .tooltip ("toggle settings")

                (HStack(5) {
                    Label "search top"

                    NumericUpDown(0, float UInt16.MaxValue, model.Top, TopChanged)
                        .formatString("F0")
                        .tooltip ("number of videos to search")

                    Label "videos"
                })
                    .centerHorizontal()
                    .isVisible (model.ShowSettings)

                (HStack(5) {
                    Label "and look for new ones after"

                    NumericUpDown(0, float UInt16.MaxValue, model.CacheHours, CacheHoursChanged)
                        .formatString("F0")
                        .tooltip (
                            "The info about which videos are in a playlist or channel is cached locally to speed up future searches."
                            + " This controls after how many hours such a cache is considered stale."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Note that this doesn't concern the video data caches,"
                            + " which are not expected to change often and are stored until you explicitly clear them."
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
