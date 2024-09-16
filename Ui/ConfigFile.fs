namespace Ui

open System.Collections.Generic
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
        | CopyShellCmd of RecentCommands.Item
        | Remove of RecentCommands.Item
        | Save
        | Common of CommonMsg

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

    let private copyShellCmd (command: OutputCommand) =
        task {
            let clipboard = FabApplication.Current.Clipboard
            let text = command.ToShellCommand()
            do! clipboard.SetTextAsync(text)
            return NotifyLong("In the clipboard:", text) |> Common
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

        | Remove cfg ->
            let model =
                { model with
                    Recent = model.Recent |> List.except [ cfg ] }

            model, save model |> Cmd.OfTask.msgOption

        | CopyShellCmd cmd -> model, copyShellCmd cmd.Command |> Cmd.OfTask.msg
        | Save -> model, Cmd.none
        | Load _ -> model, Cmd.none
        | Common _ -> model, Cmd.none

    let view model =
        ListBox(
            model.Recent,
            (fun config ->
                (Grid(coldefs = [ Star; Auto; Auto; Auto ], rowdefs = [ Auto ]) {
                    TextBlock(config.Description)
                        .tappable(Load config.Command, "load this command")
                        .wrap ()

                    Button("📋", CopyShellCmd config)
                        .tooltip("copy shell command to clipboard")
                        .gridColumn (1)

                    TextBlock(config.LastRun.ToString()).tooltip("last run").gridColumn (2)
                    Button("❌", Remove config).tooltip("forget about this").gridColumn (3)
                }))
        )
