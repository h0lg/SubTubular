﻿namespace SubTubular.Gui

open System
open System.Linq
open Avalonia.Controls
open Fabulous
open Fabulous.Avalonia
open SubTubular
open type Fabulous.Avalonia.View

module Scopes =
    type Model = { List: Scope.Model list }

    type Msg =
        | AddScope of Type
        | ScopeMsg of Scope.Model * Scope.Msg
        | Common of CommonMsg

    let private mapScopeCmd scopeModel scopeCmd =
        Cmd.map (fun scopeMsg -> ScopeMsg(scopeModel, scopeMsg)) scopeCmd

    let init () = { List = [] }

    let loadRecentCommand model (command: OutputCommand) =
        let scopes =
            command.GetScopes()
            |> Seq.map Scope.recreateRecent
            (*  turn into local collection before iterating over it
                to avoid triggering recreation and validation of scopes *)
            |> List.ofSeq

        { model with
            List = scopes |> List.map fst },
        scopes
        |> List.map (fun scope ->
            let model, cmd = scope
            mapScopeCmd model cmd)
        |> Cmd.batch

    let private getScopes<'T> model =
        model.List.Where(_.Scope.IsValid).Select(_.Scope).OfType<'T>().ToArray()

    let setOnCommand model (command: OutputCommand) =
        command.Channels <- getScopes<ChannelScope> model
        command.Playlists <- getScopes<PlaylistScope> model
        let videos = getScopes<VideosScope> model |> Seq.tryExactlyOne

        if videos.IsSome then
            command.Videos <- videos.Value

    let update msg model =
        match msg with
        | AddScope ofType ->
            { model with
                List = model.List @ [ Scope.add ofType ] },
            Cmd.none

        | ScopeMsg(scope, scopeMsg) ->
            let updatedScope, cmd, intent = Scope.update scopeMsg scope

            let updated =
                match intent with
                | Scope.Intent.RemoveMe ->
                    { model with
                        List = model.List |> List.except [ updatedScope ] }
                | Scope.Intent.DoNothing ->
                    { model with
                        List = model.List |> List.map (fun s -> if s = scope then updatedScope else s) }

            let mappedCmd = mapScopeCmd updatedScope cmd

            let fwdCmd =
                match scopeMsg with
                | Scope.Msg.Common cmsg -> Cmd.batch [ mappedCmd; Common cmsg |> Cmd.ofMsg ]
                | _ -> mappedCmd

            updated, fwdCmd
        | Common _ -> model, Cmd.none

    let private getAddableTypes model =
        let multipleAllowed = [ typeof<ChannelScope>; typeof<PlaylistScope> ]

        if model.List |> List.exists Scope.isForVideos then
            multipleAllowed
        else
            multipleAllowed @ [ typeof<VideosScope> ]

    let private addScopeStack = ViewRef<Border>()
    let private container = ViewRef<Panel>()

    let view model showThumbnails =
        (Panel() {
            HWrap() {
                let maxWidth =
                    match container.TryValue with
                    | Some panel -> panel.DesiredSize.Width
                    | None -> infinity

                for scope in model.List do
                    (Border(
                        View.map (fun scopeMsg -> ScopeMsg(scope, scopeMsg)) (Scope.view scope maxWidth showThumbnails)
                    ))
                        .classes("scope")
                        .top()
                        .padding(2)
                        .margin(0, 0, 5, 5)
                        .cornerRadius (2)

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
                        Button(ScopeViews.displayType scopeType true, AddScope scopeType)
                }
            ))
                .classes("add-scope")
                .padding(2)
                .reference(addScopeStack)
                .bottom()
                .right()
                .cornerRadius (2)
        })
            .reference (container)
