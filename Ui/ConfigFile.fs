namespace Ui

open System.Collections.Generic
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular

open type Fabulous.Avalonia.View

module ConfigFile =
    type Model = { Recent: RecentCommands.Item list }

    type Msg =
        | RecentsLoaded of RecentCommands.Item list
        | CommandRun of OutputCommand
        | Load of OutputCommand
        | Save

    let loadRecent =
        task {
            let! recent = RecentCommands.ListAsync()
            return RecentsLoaded(List.ofSeq recent)
        }

    let private save model =
        task {
            do! RecentCommands.SaveAsync(model.Recent)
            return None
        }

    let initModel = { Recent = [] }

    let update msg model =
        match msg with
        | RecentsLoaded list -> { model with Recent = list }, Cmd.none

        | CommandRun cmd ->
            let list = List<RecentCommands.Item>(model.Recent) // convert to generic List to enable reusing AddOrUpdate
            list.AddOrUpdate(cmd) // sorts the list as well
            let model = { model with Recent = List.ofSeq list } // update our model
            model, save model |> Cmd.OfTask.msgOption

        | Save -> model, Cmd.none
        | Load _ -> model, Cmd.none

    let view model =
        ListBox(
            model.Recent,
            (fun config ->
                (Grid(coldefs = [ Star; Auto; Auto ], rowdefs = [ Auto ]) {
                    TextBlock(config.Description)
                        .tappable(Load config.Command, "load this command")
                        .textWrapping (TextWrapping.Wrap)

                    TextBlock(config.LastRun.ToString()).tooltip("last run").gridColumn (1)
                }))
        )
