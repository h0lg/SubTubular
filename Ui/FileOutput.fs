namespace Ui

open System
open System.Threading
open Avalonia.Controls
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open type Fabulous.Avalonia.View

module FileOutput =
    type Open= nothing = 0 | file = 1 | folder = 2

    type Model = {
        Html: bool
        To: string
        Opening: Open
    }

    type Msg =
        | HtmlChanged of bool
        | ToChanged of string
        | OpenChanged of SelectionChangedEventArgs
        | SaveOutput

    let init () =
        {
            Html = true
            To = Folder.GetPath Folders.output
            Opening = Open.nothing
        }

    let save command orderedResults =
        async {
            CommandValidator.PrevalidateSearchCommand command
            let cacheFolder = Folder.GetPath Folders.cache
            let dataStore = JsonFileDataStore cacheFolder
            let youtube = Youtube(dataStore, VideoIndexRepository cacheFolder)
            use cts = new CancellationTokenSource()
            do! CommandValidator.ValidateScopesAsync(command, youtube, dataStore, cts.Token) |> Async.AwaitTask

            let writer = if command.OutputHtml then new HtmlOutputWriter(command) :> FileOutputWriter else new TextOutputWriter(command)
            writer.WriteHeader()

            for result in orderedResults do
                writer.WriteVideoResult(result, command.Padding |> uint32)

            // turn ValueTask into Task while native ValueTask handling is in RFC, see https://stackoverflow.com/a/52398452
            let! path = writer.SaveFile().AsTask() |> Async.AwaitTask

            match writer with
            | :? HtmlOutputWriter as htmlWriter -> htmlWriter.Dispose()
            | :? TextOutputWriter as textWriter -> textWriter.Dispose()
            | _ -> failwith "Unknown output writer type."

            // spare the user some file browsing
            if command.Show.HasValue then
                match command.Show.Value with
                | OutputCommand.Shows.file -> ShellCommands.OpenFile path
                | OutputCommand.Shows.folder -> ShellCommands.ExploreFolder path |> ignore
                | _ -> failwith $"Unknown {nameof OutputCommand.Shows} value"

            return path
        }

    let update msg model =
        match msg with
        | HtmlChanged value -> { model with Html = value }
        | ToChanged path -> { model with To = path }
        | OpenChanged args -> { model with Opening = args.AddedItems.Item 0 :?> Open }
        | SaveOutput -> model

    let private displayOpen = function
    | Open.nothing -> "nothing"
    | Open.file -> "📄 file"
    | Open.folder -> "📂 folder"
    | _ -> failwith "unknown Show Option"

    let view model =
        Grid(coldefs = [Auto; Auto; Auto; Star; Auto; Auto; Auto], rowdefs = [Auto]) {
            Label("ouput")
            ToggleButton((if model.Html then "🖺 html" else "🖹 text"), model.Html, HtmlChanged).gridColumn(1)
            Label("to").gridColumn(2)
            TextBox(model.To, ToChanged).watermark("where to save the output file").gridColumn(3)
            Label("and open").gridColumn(4)
            ComboBox(Enum.GetValues<Open>(), fun show -> ComboBoxItem(displayOpen show))
                .selectedItem(model.Opening).onSelectionChanged(OpenChanged).gridColumn(5)
            Button("💾 Save", SaveOutput).gridColumn(6)
        }
