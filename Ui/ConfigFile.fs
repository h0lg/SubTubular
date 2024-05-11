namespace Ui

open System.Collections.Generic
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular

open type Fabulous.Avalonia.View

module ConfigFile =
    type Model = { Recent: List<RecentCommands.Item> }

    type Msg =
        | RecentsLoaded of List<RecentCommands.Item>
        | CommandRun of OutputCommand
        | Load of OutputCommand
        | Save

    let loadRecent =
        task {
            let! recent = RecentCommands.ListAsync()
            return RecentsLoaded recent
        }

    let private save model =
        task {
            do! RecentCommands.SaveAsync(model.Recent)
            return None
        }

    let initModel = { Recent = List<RecentCommands.Item>() }

    let update msg model =
        match msg with
        | RecentsLoaded list -> { model with Recent = list }, Cmd.none

        | CommandRun cmd ->
            model.Recent.AddOrUpdate(cmd)
            model, save model |> Cmd.OfTask.msgOption

        | Save -> model, Cmd.none
        | Load _ -> model, Cmd.none

    let view model =
        ListBox(
            model.Recent,
            (fun config ->
                (Grid(coldefs = [ Star; Auto; Auto ], rowdefs = [ Auto ]) {
                    TextBlock(config.Description).textWrapping (TextWrapping.Wrap)
                    TextBlock(config.LastRun.ToString()).tip(ToolTip("last run")).gridColumn (1)
                    Button("Load", Load config.Command).gridColumn (2)
                }))
        )
