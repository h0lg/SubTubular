namespace Ui

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module Scopes =
    type Model =
        { List: Scope.Model list
          Youtube: Youtube }

    type Msg =
        | AddScope of Scope.Type
        | ScopeMsg of Scope.Model * Scope.Msg

    let init youtube = { List = []; Youtube = youtube }

    let updateFromCommand model (command: OutputCommand) =
        let list =
            seq {
                for c in
                    command.Channels
                    |> Seq.map (fun c ->
                        Scope.init
                            Scope.Type.channel
                            c.Alias
                            model.Youtube
                            (Some(c.Top |> float))
                            (Some(c.CacheHours |> float))) do
                    yield c

                for p in
                    command.Playlists
                    |> Seq.map (fun p ->
                        Scope.init
                            Scope.Type.playlist
                            p.Alias
                            model.Youtube
                            (Some(p.Top |> float))
                            (Some(p.CacheHours |> float))) do
                    yield p

                if command.Videos <> null && command.Videos.Videos.Length > 0 then
                    let aliases = command.Videos.Videos |> Scope.VideosInput.join
                    yield Scope.init Scope.Type.videos aliases model.Youtube None None
            }

        { model with List = list |> List.ofSeq }

    let setOnCommand model (command: OutputCommand) =
        let getScopes ofType =
            model.List
            |> List.filter (fun s -> s.Type = ofType && s.Aliases.IsNonWhiteSpace())

        let getPlaylistLikeScopes ofType =
            getScopes ofType
            |> List.map (fun scope ->
                (Scope.Alias.clean scope.Aliases, scope.Top.Value |> uint16, scope.CacheHours.Value |> float32))

        command.Channels <-
            getPlaylistLikeScopes Scope.Type.channel
            |> List.map ChannelScope
            |> List.toArray

        command.Playlists <-
            getPlaylistLikeScopes Scope.Type.playlist
            |> List.map PlaylistScope
            |> List.toArray

        let videos = getScopes Scope.Type.videos |> List.tryExactlyOne

        if videos.IsSome then
            command.Videos <- VideosScope(Scope.VideosInput.splitAndClean videos.Value.Aliases)

    let update msg model =
        match msg with
        | AddScope ofType ->
            { model with
                List = model.List @ [ Scope.add ofType model.Youtube ] }

        | ScopeMsg(scope, scopeMsg) ->
            let updated, intent = Scope.update scopeMsg scope

            match intent with
            | Scope.Intent.RemoveMe ->
                { model with
                    List = model.List |> List.except [ updated ] }
            | Scope.Intent.DoNothing ->
                { model with
                    List = model.List |> List.map (fun s -> if s = scope then updated else s) }

    // takes a batch of progresses and applies them to the model
    let updateSearchProgress (progresses: BatchProgress list) model =
        let videoLists = progresses |> List.collect (fun p -> p.VideoLists |> List.ofSeq)

        let scopes =
            model.List
            |> List.map (fun scope ->
                let matching = videoLists |> List.filter (fun pair -> Scope.matches scope pair.Key)

                match matching with
                | [] -> scope // no match, return unaltered scope
                | _ ->
                    // only apply most completed progress; batch may contain stale reports due to batched throttling
                    let scopeProgress = matching |> List.maxBy (fun pair -> pair.Value.CompletedJobs)

                    { scope with
                        Progress = Some scopeProgress.Value })

        { model with List = scopes }

    let private getAddableTypes model =
        let allTypes = Enum.GetValues<Scope.Type>()

        if model.List |> List.exists Scope.isForVideos then
            allTypes |> Array.except [ Scope.Type.videos ]
        else
            allTypes

    let private addScopeStack = ViewRef<Border>()

    let view model =
        Panel() {
            HWrap() {
                Label "in"

                for scope in model.List do
                    (Border(View.map (fun scopeMsg -> ScopeMsg(scope, scopeMsg)) (Scope.view scope)))
                        .padding(2)
                        .margin(0, 0, 5, 5)
                        .cornerRadius(2)
                        .background (Colors.DarkSlateGray)

                (*  Render an empty spacer the size of the add scope control stack,
                    effectively creating an empty line in the HWrap if they don't fit the current one.
                    This prevents the absolutely positioned add controls from overlapping the last scope input. *)
                match addScopeStack.TryValue with
                | Some stack -> Rectangle().width(stack.DesiredSize.Width).height (stack.DesiredSize.Height)
                | None -> ()
            }

            (Border(
                HStack(5) {
                    Label "add"

                    for scopeType in getAddableTypes model do
                        Button(Scope.displayType scopeType, AddScope scopeType)
                }
            ))
                .padding(2)
                .reference(addScopeStack)
                .verticalAlignment(VerticalAlignment.Bottom)
                .horizontalAlignment(HorizontalAlignment.Right)
                .cornerRadius(2)
                .background (Colors.Gray)
        }
