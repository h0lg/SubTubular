namespace SubTubular.Gui

open System
open System.IO
open Fabulous
open Fabulous.Avalonia
open SubTubular

open type Fabulous.Avalonia.View

module Cache =
    type LastAccessGroup = { Name: string; Files: FileInfo list }

    type Model =
        { Folders: Folder.View array option
          ByLastAccess: LastAccessGroup list option }

    type Msg =
        | ExpandingByLastAccess
        | RemoveByLastAccess of LastAccessGroup
        | RemoveFromLastAccessGroup of LastAccessGroup * FileInfo list
        | ExpandingFolders
        | OpenFolder of string

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

    let initModel = { Folders = None; ByLastAccess = None }

    let update msg model =
        match msg with
        | ExpandingByLastAccess ->
            let model =
                if model.ByLastAccess.IsSome then
                    model
                else
                    { model with
                        ByLastAccess = loadByLastAccess () |> Some }

            model, Cmd.none

        | RemoveByLastAccess group ->
            for file in group.Files do
                file.Delete()

            { model with
                ByLastAccess = model.ByLastAccess.Value |> List.except ([ group ]) |> Some },
            Cmd.none

        | RemoveFromLastAccessGroup(group, files) ->
            for file in files do
                file.Delete()

            { model with
                ByLastAccess =
                    model.ByLastAccess.Value
                    |> List.map (fun g ->
                        if g = group then
                            { group with
                                Files = group.Files |> List.except files }
                        else
                            g)
                    |> Some },
            Cmd.none

        | ExpandingFolders ->
            let model =
                if model.Folders.IsSome then
                    model
                else
                    { model with
                        Folders = Folder.DisplayList() |> Some }

            model, Cmd.none

        | OpenFolder path ->
            ShellCommands.ExploreFolder path |> ignore
            model, Cmd.none

    let private filterFilesByExtension extension group =
        group.Files |> List.filter (fun f -> f.Extension = extension)

    let private reportFiles label group (files: FileInfo list) =
        let mbs = (files |> List.map _.Length |> List.sum |> float32) / 1024f / 1024f

        (HStack(5) {
            TextBlock($"{files.Length} {label} ({mbs:f2} Mb)").centerVertical ()

            Button("🗑", RemoveFromLastAccessGroup(group, files))
                .tooltip ("clear these files")
        })
            .right()
            .isVisible (files.Length > 0)

    let view model =
        Grid(coldefs = [ Star; Auto ], rowdefs = [ Star ]) {

            Expander(
                "Files accessed within the last...",
                ScrollViewer(
                    ItemsControl(
                        model.ByLastAccess |> Option.defaultValue [],
                        fun group ->
                            (Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Star ]) {
                                TextBlock(group.Name).header ()

                                Button("🗑", RemoveByLastAccess group)
                                    .tooltip("clear this data")
                                    .fontSize(20)
                                    .gridRow (1)

                                (VStack(5) {
                                    let jsonFiles = group |> filterFilesByExtension JsonFileDataStore.FileExtension

                                    let videos =
                                        jsonFiles |> List.filter (fun f -> f.Name.StartsWith(Video.StorageKeyPrefix))

                                    let playlistsAndChannels = jsonFiles |> List.except videos

                                    group
                                    |> filterFilesByExtension VideoIndexRepository.FileExtension
                                    |> reportFiles "full-text indexes" group

                                    reportFiles "playlist and channel caches" group playlistsAndChannels
                                    reportFiles "video caches" group videos
                                    group |> filterFilesByExtension "" |> reportFiles "thumbnails" group
                                })
                                    .gridRowSpan(2)
                                    .gridColumn (1)
                            })
                                .margin (10)
                    )
                )
            )
                .onExpanding(fun _ -> ExpandingByLastAccess)
                .top ()

            Expander(
                "Locations",
                ItemsControl(
                    model.Folders |> Option.defaultValue [||],
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
