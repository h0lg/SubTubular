namespace SubTubular.Gui

open System.IO
open Fabulous
open Fabulous.Avalonia
open SubTubular
open type Fabulous.Avalonia.View

module CachePage =
    open Cache

    type FileType = { Label: string; Description: string }

    type Model =
        { FileTypes: FileType array
          Folders: Folder.View array option
          ByLastAccess: LastAccessGroup list option
          ByPlaylist: CacheByPlaylist.Model option
          LoadingByPlaylist: bool }

    type Msg =
        | ExpandingByLastAccess
        | ExpandingByPlaylist
        | RemoveByLastAccess of LastAccessGroup
        | RemoveFromLastAccessGroup of LastAccessGroup * FileInfo array
        | ExpandingFolders
        | OpenFolder of string
        | CacheByPlaylistMsg of CacheByPlaylist.Msg
        | CacheMsg of Cache.Msg

    let private fileTypes =
        [| { Label = "video caches" + Icon.videoCache
             Description = "Original video metadata including captions. Expected to rarely change." }
           { Label = "thumbnails" + Icon.thumbnail
             Description = "Images downloaded by the UI for displaying thumbnails. Expected to rarely change." }
           { Label = "playlist and channel caches" + Icon.playlistLike
             Description =
               "Metadata about videos contained and indexes built for them."
               + " Created on the first search and updated on following searches when outdated according to the specified cache hours." }
           { Label = "full-text indexes" + Icon.index
             Description =
               "Metadata about the contents of one or multiple video metadata caches that allows full-text searching them quickly."
               + " Created or enhanced from the downloaded video caches when searching them. Cheap to re-create from already downloaded data." }
           { Label = "searches" + Icon.scopeSearch
             Description = "Caches used to speed up scope searches." } |]

    let initModel =
        { FileTypes = fileTypes
          Folders = None
          ByLastAccess = None
          ByPlaylist = None
          LoadingByPlaylist = false }

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

        | ExpandingByPlaylist ->
            { model with LoadingByPlaylist = true },
            if model.LoadingByPlaylist then
                Cmd.none
            else
                CacheByPlaylist.load () |> Cmd.map CacheByPlaylistMsg

        | CacheByPlaylistMsg plMsg ->
            let plModel, fwdMsg =
                match plMsg with
                | CacheByPlaylist.Msg.Loaded pl -> pl, None
                | CacheByPlaylist.Msg.CacheMsg cmsg -> model.ByPlaylist.Value, CacheMsg cmsg |> Some
                | _ -> model.ByPlaylist.Value, None

            let updated = CacheByPlaylist.update plMsg plModel

            { model with
                ByPlaylist = updated |> Some },
            match fwdMsg with
            | Some fwd -> Cmd.ofMsg fwd
            | None -> Cmd.none

        | RemoveByLastAccess group ->
            { model with
                ByLastAccess = model.ByLastAccess.Value |> List.except ([ group ]) |> Some },
            group.GetFiles() |> Array.ofSeq |> RemoveFiles |> CacheMsg |> Cmd.ofMsg

        | RemoveFromLastAccessGroup(group, files) ->
            { model with
                ByLastAccess =
                    model.ByLastAccess.Value
                    |> List.map (fun g -> if g = group then g.Remove(files) else g)
                    |> Some },
            RemoveFiles files |> CacheMsg |> Cmd.ofMsg

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

        | CacheMsg msg ->
            let updated =
                match msg with
                | Cache.Msg.OpenFile file ->
                    ShellCommands.OpenFile file.FullName
                    model

                | Cache.Msg.RemoveFiles files ->
                    for file in files do
                        file.Delete()

                    { model with
                        ByLastAccess =
                            match model.ByLastAccess with
                            | Some la -> la.Remove(files) |> List.ofSeq |> Some
                            | None -> None
                        ByPlaylist =
                            match model.ByPlaylist with
                            | Some bpl -> CacheByPlaylist.remove bpl files |> Some
                            | None -> None }

            updated, Cmd.none

    let private displayFileTypes model =
        ItemsControl(
            model.FileTypes,
            fun fileType ->
                (Grid(coldefs = [ Pixel(200); Star ], rowdefs = [ Auto ]) {
                    Label(fileType.Label)
                    TextBlock(fileType.Description).wrap().demoted().gridColumn (1)
                })
        )

    let private folders model =
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

    let private byLastAccess model =
        ItemsControl(
            model.ByLastAccess |> Option.defaultValue [],
            fun group ->
                (Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Star ]) {
                    TextBlock(group.TimeSpanLabel).header ()

                    Button(Icon.trash, RemoveByLastAccess group).tooltip("clear this data").fontSize(20).gridRow (1)

                    (VStack(5) {
                        let report label files =
                            reportFiles label (RemoveFromLastAccessGroup(group, files)) files

                        report ("full-text indexes" + Icon.index) group.Indexes
                        report ("playlist and channel caches" + Icon.playlistLike) group.PlaylistLikes
                        report ("video caches" + Icon.videoCache) group.Videos
                        report ("thumbnails" + Icon.thumbnail) group.Thumbnails
                        report ("searches" + Icon.scopeSearch) group.Searches
                    })
                        .gridRowSpan(2)
                        .gridColumn (1)
                })
                    .card()
                    .margin (10)
        )
            .itemsPanel (HWrapEmpty())

    let view model =
        ScrollViewer(
            let gap = Pixel 10

            Grid(coldefs = [ Star; gap; Auto ], rowdefs = [ Auto; gap; Auto; gap; Auto ]) {
                Expander("File type info " + Icon.help, displayFileTypes model)

                Expander("Go to 📂 Locations", folders model)
                    .onExpanding(fun _ -> ExpandingFolders)
                    .isExpanded(model.Folders.IsSome)
                    .gridColumn (2)

                Expander("Files by type accessed within the last... " + Icon.recent, byLastAccess model)
                    .onExpanding(fun _ -> ExpandingByLastAccess)
                    .isExpanded(model.ByLastAccess.IsSome)
                    .gridRow(2)
                    .gridColumnSpan (3)

                Expander(
                    $"Files by {Icon.channel}channel, {Icon.playlist}playlist and type",
                    View.map CacheByPlaylistMsg (CacheByPlaylist.view model.ByPlaylist)
                )
                    .onExpanding(fun _ -> ExpandingByPlaylist)
                    .isExpanded(model.ByPlaylist.IsSome)
                    .gridRow(4)
                    .gridColumnSpan (3)
            }
        )
            .center ()
