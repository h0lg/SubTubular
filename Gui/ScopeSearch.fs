namespace SubTubular.Gui

open System
open System.Linq
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Fabulous
open Fabulous.Avalonia
open SubTubular
open SubTubular.Extensions
open ScopeDiscriminators
open type Fabulous.Avalonia.View

module ScopeSearch =
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

        /// splits and partitions the input into two lists:
        /// first the remote-validated Video IDs (whether they are in the input or not),
        /// second the inputs that haven't already been remote-validated and are considered search terms
        let partition (input: string) (scope: VideosScope) =
            let remoteValidatedIds = scope.GetRemoteValidated().Ids() |> List.ofSeq

            let searchTerms =
                split input
                |> Array.map Alias.clean
                |> Array.filter (fun alias ->
                    if remoteValidatedIds.Contains alias then
                        false // exclude remote-validated ID
                    else
                        let preValidated = VideosScope.TryParseId(alias)

                        if preValidated = null then
                            true // include terms that don't pre-validate
                        else
                            remoteValidatedIds.Contains preValidated |> not) // exclude remote-validated URL

            remoteValidatedIds, searchTerms |> List.ofArray

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
            // keep search term and append selected item for playlist-like scopes
            | PlaylistLike _ -> selectedText <- text + " | " + Alias.label result.Title result.Id

            selectedText

        member this.SearchAsync (text: string) (token: CancellationToken) : Task<obj seq> =
            task {
                // only start search if input has keyboard focus & avoid re-triggering it for the same search term after selection
                if input.Value.IsKeyboardFocusWithin && text <> selectedText then
                    token.Register(cancel) |> ignore // register cancellation of running search when outer cancellation is requested
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
                    with :? OperationCanceledException when token.IsCancellationRequested ->
                        return []
                else
                    return []
            }

    type Model =
        { Scope: CommandScope
          Aliases: string
          AliasSearch: AliasSearch
          DropdownOpen: bool
          ValidationError: string
          Added: bool }

    type Msg =
        | AliasesUpdated of string
        | Populated
        | FocusToggled of bool
        | DropdownToggled of bool
        | ValidationSucceeded
        | ValidationFailed of exn

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
                let parsed = searchTerms |> List.map Alias.clean |> VideosScope.ParseIds
                let preValidatedIds, _ = parsed.ToTuple()

                let missing =
                    preValidatedIds
                    |> Seq.except vids.Videos // already added to inputs
                    |> Seq.except (vids.GetRemoteInvalidatedIds()) // inputs that pre-validate, but don't remote-validate
                    |> Seq.toArray

                if missing.Length > 0 then
                    vids.Videos.AddRange missing // to have them validated

                searchTerms |> VideosInput.join

        { model with Aliases = updatedAliases }

    /// first pre-validates the scope, then triggers remoteValidate on success
    let validate model =
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

    let init scope added =
        { Scope = scope
          Aliases = // set from scope to sync
            match scope with
            | PlaylistLike pl -> pl.Alias
            | Vids v -> v.Videos |> VideosInput.join
          DropdownOpen = false
          AliasSearch = AliasSearch(scope)
          ValidationError = null
          Added = added }

    let private openDropdown model =
        Dispatch.toUiThread (fun () ->
            match model.AliasSearch.Input.TryValue with
            | Some dropdown -> dropdown.IsDropDownOpen <- true
            | None -> ())

    let update msg model =
        match msg with
        | AliasesUpdated aliases ->
            { model with
                Added = false
                ValidationError = null
                Aliases = aliases },
            Cmd.none

        | Populated ->
            openDropdown model // to assist selection
            { model with DropdownOpen = true }, Cmd.none

        | FocusToggled gained ->
            if gained then
                openDropdown model // to assist selection
                { model with DropdownOpen = true }, Cmd.none
            else
                model.AliasSearch.Cancel() // to avoid population after losing focus

                // update model and validate to lock in selected items
                let model, cmd = syncScopeWithAliases model |> validate

                model, cmd

        | DropdownToggled isOpen ->
            let model = { model with DropdownOpen = isOpen }

            let model, cmd =
                if isOpen then
                    model, Cmd.none
                else // trigger update & validation on closing (e.g. after click) to lock in selection or display error
                    syncScopeWithAliases model |> validate

            model, cmd

        | ValidationSucceeded ->
            model.AliasSearch.IsRemoteValidating <- false

            { model with ValidationError = null } |> syncScopeWithAliases, // to remove remote-validated from input
            Cmd.none

        | ValidationFailed exn ->
            model.AliasSearch.IsRemoteValidating <- false

            { model with
                ValidationError = exn.GetRootCauses() |> Seq.map (fun ex -> ex.Message) |> String.concat "\n" },
            Cmd.none

    let private getAliasWatermark model =
        "search YouTube - or enter "
        + match model.Scope with
          | Videos _ -> "IDs or URLs; one per line"
          | Playlist _ -> "an ID or URL"
          | Channel _ -> "a handle, slug, user name, ID or URL"

    let input model showThumbnails =
        Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Auto ]) {
            Label(ScopeViews.displayType (model.Scope.GetType()) false).padding (0)

            let autoComplete =
                AutoCompleteBox(model.AliasSearch.SearchAsync)
                    .minimumPopulateDelay(TimeSpan.FromMilliseconds 300)
                    .onTextChanged(model.Aliases, AliasesUpdated)
                    .onLostFocus(fun _ -> FocusToggled false)
                    .onGotFocus(fun _ -> FocusToggled true)
                    .onPopulated(fun _ -> Populated)
                    .minimumPrefixLength(3)
                    .filterMode(AutoCompleteFilterMode.None)
                    .focus(model.Added)
                    .watermark(getAliasWatermark model)
                    .itemSelector(model.AliasSearch.SelectAliases)
                    .itemTemplate(fun (result: YoutubeSearchResult) ->
                        HStack(5) {
                            AsyncImage(result.Thumbnail).maxHeight(100).isVisible (showThumbnails)

                            VStack(5) {
                                TextBlock(result.Title)

                                if result.Channel <> null then
                                    ScopeViews.channelInfo (result.Channel)
                            }
                        })
                    .reference(model.AliasSearch.Input)
                    .margin(5, 0, 0, 0)
                    .gridColumn(1)
                    .gridRowSpan (2)

            match model.Scope with
            | Videos videos ->
                ScopeViews.progressText(videos.Progress.ToString()).gridRow (1)
                autoComplete.onDropDownOpened(model.DropdownOpen, DropdownToggled).acceptReturn ()
            | _ -> autoComplete
        }

    let validationErrors model =
        TextBlock(model.ValidationError)
            .classes("error")
            // display if there is a validation error and the model state is not valid
            .isVisible (model.ValidationError <> null && not model.Scope.IsValid)
