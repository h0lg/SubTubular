namespace SubTubular.Gui

open System
open System.Linq
open Avalonia.Controls
open Fabulous
open Fabulous.Avalonia
open SubTubular
open type Fabulous.Avalonia.View

module Scopes =
    type Model =
        {
            List: Scope.Model list

            /// tracks size of the list view for parent view to adjust the layout
            ListHeight: float

            /// indicates whether the List contains any Scope for Videos
            HasVideos: bool
        }

    type Msg =
        | AddScope of Type
        | ScopeMsg of Scope.Model * Scope.Msg
        | ListSizeChanged of SizeChangedEventArgs
        | Common of CommonMsg

    let private mapScopeCmd scopeModel scopeCmd =
        Cmd.map (fun scopeMsg -> ScopeMsg(scopeModel, scopeMsg)) scopeCmd

    let init () =
        { List = []
          ListHeight = 0
          HasVideos = false }

    let private updateList model list =
        { model with
            List = list
            HasVideos = list |> List.exists Scope.isForVideos }

    let loadRecentCommand model (command: OutputCommand) =
        // dispatch Remove msgs for current scopes so they may dispose of their resources properly
        let removeCurrentScopes =
            model.List
            |> List.map (fun scope -> ScopeMsg(scope, Scope.Msg.Remove) |> Cmd.ofMsg)

        let recreateRecentScopes =
            command.GetScopes()
            |> Seq.map Scope.recreateRecent
            |> Seq.map (fun scope ->
                let model, cmd = scope
                mapScopeCmd model cmd)
            |> List.ofSeq

        removeCurrentScopes @ recreateRecentScopes |> Cmd.batch

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
            let scope, cmd = Scope.add ofType
            let mappedCmd = mapScopeCmd scope cmd
            model, mappedCmd

        | Common _ -> model, Cmd.none

        | ScopeMsg(scope, scopeMsg) ->
            let updatedScope, scopeCmd, intent = Scope.update scopeMsg scope

            let updated =
                match intent with
                | Scope.Intent.AddMe -> model.List @ [ updatedScope ] |> updateList model
                | Scope.Intent.RemoveMe -> model.List |> List.except [ updatedScope ] |> updateList model
                | Scope.Intent.DoNothing ->
                    model.List
                    |> List.map (fun s -> if s = scope then updatedScope else s) // replace updated scope in list
                    |> updateList model

            let mappedCmd = mapScopeCmd updatedScope scopeCmd

            let fwdCmd =
                match scopeMsg with
                | Scope.Msg.Common cmsg -> Cmd.batch [ mappedCmd; Common cmsg |> Cmd.ofMsg ]
                | _ -> mappedCmd

            updated, fwdCmd

        | ListSizeChanged args ->
            let model =
                if args.HeightChanged then
                    { model with
                        ListHeight = args.NewSize.Height }
                else
                    model

            model, Cmd.none

    let list model maxWidth showThumbnails =
        (HWrap() {
            for scope in model.List do
                (Border(View.map (fun scopeMsg -> ScopeMsg(scope, scopeMsg)) (Scope.view scope maxWidth showThumbnails)))
                    .classes("scope")
        })
            .onSizeChanged(ListSizeChanged)
            .top()

    let private addButton scopeType =
        Button(ScopeViews.displayType scopeType true, AddScope scopeType)

    let addButtons model =
        (Border(
            HStack(5) {
                Label "add"
                addButton typeof<ChannelScope>
                addButton typeof<PlaylistScope>
                (addButton typeof<VideosScope>).isVisible(not model.HasVideos)
            }
        ))
            .classes("add-scope")
