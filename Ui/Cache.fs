namespace Ui

open System
open System.IO
open Fabulous
open Fabulous.Avalonia
open SubTubular

open type Fabulous.Avalonia.View

module Cache =
    type LastAccessGroup = { Name: string; Files: FileInfo list }

    type FolderView =
        { Label: string
          IndentLevel: int // to root
          PathDiff: string // diff of Path to that of the parent FolderView if any, otherwise the full Path
          Path: string }

    type Model =
        { Folders: FolderView list option
          LastAccessGroups: LastAccessGroup list option }

    type Msg =
        | ExpandingCacheByLastAccess
        | RemoveByLastAccess of LastAccessGroup
        | ExpandingFolders
        | OpenFolder of string

    let private loadFolders () =
        let label folder =
            if folder = Folders.storage then
                "user data"
            else
                folder.ToString()

        // maps a label/path tuple to a FolderView relative to already mapped ancestor FolderViews
        let mapFolderToView mapped (label, path: string) =
            let ancestor = // with matching Path
                match mapped with
                | [] -> None
                | _ -> mapped |> List.tryFind (fun prev -> path.Contains(prev.Path))

            let level, pathDiff =
                match ancestor with
                | Some ancestor ->
                    ancestor.IndentLevel + 1,
                    path
                        .Substring(ancestor.Path.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                | None -> 0, path

            let folderView =
                { Label = label
                  IndentLevel = level
                  PathDiff = pathDiff
                  Path = path }

            folderView :: mapped

        Enum.GetValues<Folders>()
        |> Array.map (fun f -> label f, Folder.GetPath f)
        |> Array.appendOne ("other releases", ReleaseManager.GetArchivePath(Folder.GetPath(Folders.app)))
        |> Seq.filter (fun pair -> snd pair |> Directory.Exists)
        |> Seq.sortBy (fun pair -> fst pair <> nameof Folders.app, snd pair) // sort app first, then by path
        |> Seq.fold mapFolderToView [] // relies on prior sorting by path
        |> List.rev // to restore correct order reversed by folding

    // Function to describe a TimeSpan into specific ranges
    let private describeTimeSpan (timeSpan: TimeSpan) =
        match timeSpan with
        | ts when ts < TimeSpan.FromDays(1.0) -> "day"
        | ts when ts < TimeSpan.FromDays(7.0) -> sprintf "%d days" (ts.Days + 1)
        | ts when ts < TimeSpan.FromDays(30.0) ->
            let weeks = ts.Days / 7

            if weeks = 0 then "week" else sprintf "%d weeks" (weeks + 1)
        | ts when ts < TimeSpan.FromDays(90.0) -> // 90 days for roughly 3 months (quarter year)
            let months = ts.Days / 30

            if months = 0 then
                "month"
            else
                sprintf "%d months" (months + 1)
        | _ -> "eon"

    let private loadByLastAccess () =
        let now = DateTime.Now

        FileHelper.EnumerateFiles(Services.CacheFolder, "*", SearchOption.AllDirectories)
        |> Seq.sortByDescending _.LastAccessTime // latest first
        |> Seq.groupBy (fun f -> now - f.LastAccessTime |> describeTimeSpan)
        |> Seq.map (fun group ->
            { Name = fst group
              Files = snd group |> List.ofSeq })
        |> List.ofSeq

    let initModel =
        { Folders = None
          LastAccessGroups = None }

    let update msg model =
        match msg with
        | ExpandingCacheByLastAccess ->
            let model =
                if model.LastAccessGroups.IsSome then
                    model
                else
                    { model with
                        LastAccessGroups = loadByLastAccess () |> Some }

            model, Cmd.none

        | RemoveByLastAccess group ->
            for file in group.Files do
                file.Delete()

            { model with
                LastAccessGroups = model.LastAccessGroups.Value |> List.except ([ group ]) |> Some },
            Cmd.none

        | ExpandingFolders ->
            let model =
                if model.Folders.IsSome then
                    model
                else
                    { model with
                        Folders = loadFolders () |> Some }

            model, Cmd.none

        | OpenFolder path ->
            ShellCommands.ExploreFolder path |> ignore
            model, Cmd.none

    let private filterFilesByExtension extension group =
        group.Files |> List.filter (fun f -> f.Extension = extension)

    let private reportFiles label (files: FileInfo list) =
        let mbs = (files |> List.map _.Length |> List.sum |> float32) / 1024f / 1024f

        TextBlock($"{files.Length} {label} ({mbs:f2} Mb)")
            .right()
            .isVisible (files.Length > 0)

    let view model =
        Grid(coldefs = [ Star; Auto ], rowdefs = [ Star ]) {

            Expander(
                "Files accessed within the last...",
                ScrollViewer(
                    ItemsControl(
                        model.LastAccessGroups |> Option.defaultValue [],
                        fun group ->
                            (Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Star ]) {
                                TextBlock(group.Name).header ()
                                Button("🗑", RemoveByLastAccess group).tooltip("clear this data").fontSize(20).gridRow (1)

                                (VStack(5) {
                                    let jsonFiles = group |> filterFilesByExtension JsonFileDataStore.FileExtension

                                    let videos =
                                        jsonFiles |> List.filter (fun f -> f.Name.StartsWith(Video.StorageKeyPrefix))

                                    let playlistsAndChannels = jsonFiles |> List.except videos

                                    group
                                    |> filterFilesByExtension VideoIndexRepository.FileExtension
                                    |> reportFiles "full-text indexes"

                                    reportFiles "playlist and channel caches" playlistsAndChannels
                                    reportFiles "video caches" videos
                                    group |> filterFilesByExtension "" |> reportFiles "thumbnails"
                                })
                                    .gridRowSpan(2)
                                    .gridColumn (1)
                            })
                                .margin (10)
                    )
                )
            )
                .onExpanding(fun _ -> ExpandingCacheByLastAccess)
                .top ()

            Expander(
                "Locations",
                ItemsControl(
                    model.Folders |> Option.defaultValue [],
                    fun folder ->
                        (Grid(coldefs = [ Pixel(100); Star ], rowdefs = [ Auto ]) {
                            let indent = folder.IndentLevel * 10
                            TextBlock(folder.Label).padding (indent, 0, 0, 0)

                            if folder.Label <> folder.PathDiff then
                                TextBlock(folder.PathDiff).padding(indent, 0, 0, 0).demoted().gridColumn (1)
                        })
                            .tappable (OpenFolder folder.Path, "open this folder")
                )
            )
                .onExpanding(fun _ -> ExpandingFolders)
                .top()
                .gridColumn (1)
        }
