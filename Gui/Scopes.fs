﻿namespace SubTubular.Gui

open System
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

    let setOnCommand model (command: OutputCommand) =
        let getScopes ofType =
            model.List
            |> List.filter (fun s -> s.Type = ofType && s.Aliases.IsNonWhiteSpace())

        let getPlaylistLikeScopes ofType =
            getScopes ofType
            |> List.map (fun scope ->
                (Scope.cleanAlias scope.Aliases, scope.Top.Value |> uint16, scope.CacheHours.Value |> float32))

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
            command.Videos <- VideosScope(videos.Value.Aliases.Split [| ',' |] |> Array.map Scope.cleanAlias)

    let update msg model =
        match msg with
        | AddScope scope ->
            { model with
                List = model.List @ [ Scope.init scope "" model.Youtube true ] }

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

    let view model =
        (VStack() {
            HWrap() {
                Label "in"

                for scope in model.List do
                    View.map (fun scopeMsg -> ScopeMsg(scope, scopeMsg)) (Scope.view scope)
            }

            HStack(5) {
                Label "add"

                for scopeType in getAddableTypes model do
                    Button(Scope.displayType scopeType, AddScope scopeType)
            }
        })
