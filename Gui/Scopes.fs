﻿namespace SubTubular.Gui

open System
open System.Linq
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module Scopes =
    type Model = { List: Scope.Model list }

    type Msg =
        | AddScope of Type
        | ScopeMsg of Scope.Model * Scope.Msg
        | OpenUrl of string

    let init () = { List = [] }

    let updateFromCommand model (command: OutputCommand) =
        let list =
            seq {
                for c in
                    command.Channels
                    |> Seq.map (fun c -> Scope.init typeof<ChannelScope> c.Alias (Some c.Top) (Some c.CacheHours)) do
                    yield c

                for p in
                    command.Playlists
                    |> Seq.map (fun p -> Scope.init typeof<PlaylistScope> p.Alias (Some p.Top) (Some p.CacheHours)) do
                    yield p

                if command.Videos <> null && command.Videos.Videos.Count > 0 then
                    let aliases = command.Videos.Videos |> Scope.VideosInput.join
                    yield Scope.init typeof<VideosScope> aliases None None
            }

        { model with List = list |> List.ofSeq }

    let private getScopes<'T> model =
        model.List
            .Where(_.Aliases.IsNonWhiteSpace())
            .Select(_.Scope)
            .OfType<'T>()
            .ToArray()

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

            let mappedCmd = Cmd.map (fun scopeMsg -> ScopeMsg(updatedScope, scopeMsg)) cmd

            let fwdCmd =
                match scopeMsg with
                | Scope.Msg.OpenUrl uri -> Cmd.batch [ mappedCmd; OpenUrl uri |> Cmd.ofMsg ]
                | _ -> mappedCmd

            updated, fwdCmd
        | OpenUrl _ -> model, Cmd.none

    let private getAddableTypes model =
        let multipleAllowed = [ typeof<ChannelScope>; typeof<PlaylistScope> ]

        if model.List |> List.exists Scope.isForVideos then
            multipleAllowed
        else
            multipleAllowed @ [ typeof<VideosScope> ]

    let private addScopeStack = ViewRef<Border>()
    let private container = ViewRef<Panel>()

    let view model =
        (Panel() {
            HWrap() {
                Label "in"

                let maxWidth =
                    match container.TryValue with
                    | Some panel -> panel.DesiredSize.Width
                    | None -> infinity

                for scope in model.List do
                    (Border(View.map (fun scopeMsg -> ScopeMsg(scope, scopeMsg)) (Scope.view scope maxWidth)))
                        .verticalAlignment(VerticalAlignment.Top)
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
                        Button(Scope.displayType scopeType true, AddScope scopeType)
                }
            ))
                .padding(2)
                .reference(addScopeStack)
                .verticalAlignment(VerticalAlignment.Bottom)
                .horizontalAlignment(HorizontalAlignment.Right)
                .cornerRadius(2)
                .background (Colors.Gray)
        })
            .reference (container)
