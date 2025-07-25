namespace SubTubular.Gui

open System
open System.Threading
open Avalonia.Controls
open Avalonia.Platform.Storage
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module FileOutput =
    type Open =
        | nothing = 0
        | file = 1
        | folder = 2

    type Model =
        { Html: bool
          To: string
          Opening: Open }

    type Msg =
        | HtmlChanged of bool
        | ToChanged of string
        | PickTo
        | OpenChanged of SelectionChangedEventArgs
        | SaveOutput

    let private defaultOutputFolder = Folder.GetPath Folders.output

    let init () =
        { Html = true
          To = defaultOutputFolder
          Opening = Open.nothing }

    let saveAsync command writeResults =
        task {
            use cts = new CancellationTokenSource()
            do! RemoteValidate.ScopesAsync(command, Services.Youtube, Services.DataStore, cts.Token)

            let writer =
                if command.OutputHtml then
                    new HtmlOutputWriter(command) :> FileOutputWriter
                else
                    new TextOutputWriter(command)

            writer.WriteHeader()
            writeResults writer

            // turn ValueTask into Task while native ValueTask handling is in RFC, see https://stackoverflow.com/a/52398452
            let! path = writer.SaveFile().AsTask()

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
        |> Async.AwaitTask

    let private pickTo (lastOpenedFolder: string) =
        task {
            let options = FolderPickerOpenOptions()
            options.Title <- "Select an output folder"

            let startIn =
                if lastOpenedFolder.IsNonWhiteSpace() && lastOpenedFolder.IsDirectoryPath() then
                    lastOpenedFolder
                else
                    defaultOutputFolder

            let! dir = Services.Storage.TryGetFolderFromPathAsync(startIn)
            options.SuggestedStartLocation <- dir

            let! folders = Services.Storage.OpenFolderPickerAsync(options)

            let path =
                match Seq.tryExactlyOne folders with
                | Some folder -> folder.TryGetLocalPath()
                | None -> null // effectively defaultOutputFolder

            return ToChanged path
        }

    let update msg model =
        match msg with
        | HtmlChanged value -> { model with Html = value }, Cmd.none
        | ToChanged path -> { model with To = path }, Cmd.none
        | PickTo -> model, Cmd.OfTask.msg (pickTo model.To)
        | OpenChanged args ->
            { model with
                Opening = args.AddedItems.Item 0 :?> Open },
            Cmd.none
        | SaveOutput -> model, Cmd.none

    let private displayOpen =
        function
        | Open.nothing -> "nothing"
        | Open.file -> "📄 file"
        | Open.folder -> "📂 folder"
        | _ -> failwith "unknown Open Option"

    let view model =
        Grid(coldefs = [ Auto; Auto; Auto; Star; Auto; Auto; Auto; Auto ], rowdefs = [ Auto ]) {
            Label("ouput")
            ToggleButton((if model.Html then "🖺 html" else "🖹 text"), model.Html, HtmlChanged).gridColumn (1)
            Label("to").gridColumn (2)

            TextBox(model.To, ToChanged)
                .watermark("where to save the output file")
                .tooltip(OutputCommand.FileOutputPathHint + OutputCommand.ExistingFilesAreOverWritten)
                .gridColumn (3)

            Button("📂", PickTo).tooltip("Pick output folder").gridColumn (4)
            Label("and open").gridColumn (5)

            ComboBox(Enum.GetValues<Open>(), (fun show -> ComboBoxItem(displayOpen show)))
                .selectedItem(model.Opening)
                .onSelectionChanged(OpenChanged)
                .gridColumn (6)

            Button("💾 Save", SaveOutput).gridColumn (7)
        }
