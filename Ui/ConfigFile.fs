namespace Ui

open System
open Avalonia.Controls
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular

open type Fabulous.Avalonia.View

module ConfigFile =

    /// Represents info about a configuration file.
    type Info = { Name: string }

    type Model = { Recent: Info list }

    type Msg =
        | RecentsLoaded of Info array
        | Load of OutputCommand
        | Selected of SelectionChangedEventArgs

    let loadRecent =
        task {
            let! recent = RecentCommand.ListAsync()
            return RecentsLoaded(recent |> Array.map (fun name -> { Name = name }))
        }

    let private load info =
        task {
            let! cmd = RecentCommand.LoadAsync(info.Name)
            return Load cmd
        }

    let initModel = { Recent = [ { Name = "no recent config" } ] }

    let update msg model =
        match msg with
        | RecentsLoaded list ->
            { model with
                Recent = list |> List.ofArray },
            Cmd.none

        | Selected args ->
            let control = args.Source :?> ListBox

            match control.SelectedItem with
            | :? Info as info -> model, load info |> Cmd.OfTask.msg
            | _ -> model, Cmd.none

        | Load _ -> model, Cmd.none

    let view model =
        ListBox(model.Recent, (fun config -> ListBoxItem(config.Name)))
            .selectedItem(model.Recent.Head)
            .onSelectionChanged (fun args -> Selected args)
