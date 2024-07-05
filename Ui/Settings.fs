namespace Ui

open System.IO
open System.Text.Json
open Fabulous
open SubTubular

open type Fabulous.Avalonia.View

//TODO also see instead https://docs.fabulous.dev/advanced/saving-and-restoring-app-state
module Settings =
    type Model =
        { ResultOptions: ResultOptions.Model option
          FileOutput: FileOutput.Model option }

    type Msg =
        | Save
        | Saved
        | Loaded of Model

    let private path = Path.Combine(Folder.GetPath Folders.storage, "ui-settings.json")
    let requestSave = Cmd.debounce 1000 (fun () -> Save)

    let load =
        async {
            if File.Exists path then
                let! json = File.ReadAllTextAsync(path) |> Async.AwaitTask
                let settings = JsonSerializer.Deserialize json
                return Loaded settings |> Some
            else
                return None
        }
        |> Cmd.OfAsync.msgOption

    let save model =
        async {
            let json = JsonSerializer.Serialize model
            do! File.WriteAllTextAsync(path, json) |> Async.AwaitTask
            return Saved
        }
        |> Cmd.OfAsync.msg
