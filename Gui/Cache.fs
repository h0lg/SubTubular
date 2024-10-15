namespace SubTubular.Gui

open System
open System.IO
open Fabulous
open Fabulous.Avalonia
open SubTubular

open type Fabulous.Avalonia.View

module Cache =
    type Model =
        { Folders: Folder.View array option
          ByLastAccess: LastAccessGroup list option }

    type Msg =
        | ExpandingByLastAccess
        | RemoveByLastAccess of LastAccessGroup
        | RemoveFromLastAccessGroup of LastAccessGroup * FileInfo array
        | ExpandingFolders
        | OpenFolder of string

    let initModel = { Folders = None; ByLastAccess = None }

    let update msg model =
        match msg with
        | ExpandingByLastAccess ->
            let model =
                if model.ByLastAccess.IsSome then
                    model
                else
                    { model with
                        ByLastAccess = CacheManager.LoadByLastAccess(Services.CacheFolder) |> List.ofSeq |> Some }

            model, Cmd.none

        | RemoveByLastAccess group ->
            for file in group.GetFiles() do
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
                    |> List.map (fun g -> if g = group then g.Remove(files) else g)
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

    let private reportFiles label group (files: FileInfo array) =
        let mbs = (files |> Array.map _.Length |> Array.sum |> float32) / 1024f / 1024f

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
                                TextBlock(group.TimeSpanLabel).header ()

                                Button("🗑", RemoveByLastAccess group)
                                    .tooltip("clear this data")
                                    .fontSize(20)
                                    .gridRow (1)

                                (VStack(5) {
                                    let report label files = reportFiles label group files
                                    report "full-text indexes" group.Indexes
                                    report "playlist and channel caches" group.PlaylistLikes
                                    report "video caches" group.Videos
                                    report "thumbnails" group.Thumbnails
                                    report "searches" group.Searches
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
