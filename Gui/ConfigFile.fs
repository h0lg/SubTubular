namespace SubTubular.Gui

open System.Collections.Generic
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open SubTubular.Extensions

open type Fabulous.Avalonia.View

module ConfigFile =
    type Model =
        { DescriptionContains: string
          Filtered: RecentCommands.Item list
          Recent: RecentCommands.Item list }

    type Msg =
        | RecentsLoaded of RecentCommands.Item list
        | DescriptionContainsChanged of string
        | Filter
        | CommandRun of OutputCommand
        | Load of OutputCommand
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
        |> Cmd.OfTask.msgOption

    let private filter = Cmd.ofMsg Filter

    let initModel =
        { DescriptionContains = ""
          Filtered = []
          Recent = [] }

    let update msg model =
        match msg with
        | RecentsLoaded list -> { model with Recent = list }, filter
        | DescriptionContainsChanged str -> { model with DescriptionContains = str }, filter

        | Filter ->
            let filtered =
                if model.DescriptionContains.IsNullOrWhiteSpace() then
                    model.Recent
                else
                    model.Recent
                    |> List.filter (fun r ->
                        r.Description.Contains(model.DescriptionContains, System.StringComparison.OrdinalIgnoreCase))

            { model with Filtered = filtered }, Cmd.none

        | CommandRun cmd ->
            let list = List<RecentCommands.Item>(model.Recent) // convert to generic List to enable reusing AddOrUpdate
            list.AddOrUpdate(cmd) // sorts the list as well
            let model = { model with Recent = List.ofSeq list } // update our model
            model, Cmd.batch [ filter; save model ]

        | Remove cfg ->
            let model =
                { model with
                    Recent = model.Recent |> List.except [ cfg ] }

            model, Cmd.batch [ filter; save model ]

        | Save -> model, Cmd.none
        | Load _ -> model, Cmd.none
        | Common _ -> model, Cmd.none

    let view model =
        Grid(coldefs = [ Star ], rowdefs = [ Auto; Star ]) {
            TextBox(model.DescriptionContains, DescriptionContainsChanged)
                .watermark("Filter this list")
                .trailingMargin ()

            ListBox(
                model.Filtered,
                (fun config ->
                    (Grid(coldefs = [ Star; Auto; Auto; Auto ], rowdefs = [ Auto ]) {
                        TextBlock(config.Description)
                            .tappable(Load config.Command, "load this command")
                            .wrap ()

                        Button(Icon.copy, CopyShellCmd config.Command |> Common)
                            .tooltip("copy shell command to clipboard")
                            .gridColumn (1)

                        TextBlock(config.LastRun.ToString()).tooltip("last run").gridColumn (2)
                        Button("❌", Remove config).tooltip("forget about this").gridColumn (3)
                    }))
            )
                .gridRow (1)
        }
