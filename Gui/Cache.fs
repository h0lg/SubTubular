namespace SubTubular.Gui

open System
open System.IO
open AsyncImageLoader.Loaders
open Fabulous
open Fabulous.Avalonia
open SubTubular
open SubTubular.Extensions

open type Fabulous.Avalonia.View

module Cache =
    type ThumbnailCache(folder: string) =
        inherit DiskCachedWebImageLoader(folder)
        static member getFileName url = DiskCachedWebImageLoader.CreateMD5 url

    type FileType = { Label: string; Description: string }

    type GroupsByPlaylist =
        { ScopeSearches: ScopeSearches
          Channels: PlaylistGroup list
          Playlists: PlaylistGroup list
          LooseFiles: LooseFiles option }

    type Model =
        { FileTypes: FileType array
          Folders: Folder.View array option
          ByLastAccess: LastAccessGroup list option
          ByPlaylist: GroupsByPlaylist option
          LoadingByPlaylist: bool }

    type Msg =
        | ExpandingByLastAccess
        | ExpandingByPlaylist
        | ByPlaylistLoaded of GroupsByPlaylist
        | PlaylistGroupLoaded of PlaylistGroup
        | LooseFilesLoaded of LooseFiles
        | RemoveFiles of FileInfo array
        | RemoveByLastAccess of LastAccessGroup
        | RemoveFromLastAccessGroup of LastAccessGroup * FileInfo array
        | ExpandingFolders
        | OpenFolder of string
        | OpenFile of FileInfo

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

    let private loadByPlaylist () =
        fun dispatch ->
            task {
                let scopeSearches, processAsync =
                    CacheManager
                        .LoadByPlaylist(
                            Services.CacheFolder,
                            Services.Youtube,
                            (fun url -> ThumbnailCache.getFileName url)
                        )
                        .ToTuple()

                let groups =
                    { ScopeSearches = scopeSearches
                      Channels = []
                      Playlists = []
                      LooseFiles = None }

                groups |> ByPlaylistLoaded |> dispatch

                do!
                    processAsync.Invoke(
                        (fun group -> group |> PlaylistGroupLoaded |> dispatch),
                        (fun looseVids -> looseVids |> LooseFilesLoaded |> dispatch),
                        (fun exn ->
                            ErrorLog.WriteAsync(exn.ToString()).GetAwaiter().GetResult() |> ignore

                            Notify.error "Error loading playlists" "An error log was written to the errors folder."
                            |> ignore)
                    )
            }
            |> Async.AwaitTask
            |> Async.Start
        |> Cmd.ofEffect

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
                loadByPlaylist ()

        | ByPlaylistLoaded groups ->
            { model with
                ByPlaylist = groups |> Some },
            Cmd.none

        | PlaylistGroupLoaded group ->
            let updated =
                match model.ByPlaylist with
                | Some groups ->
                    let updated =
                        if group.Playlist.Channel = null then
                            { groups with
                                Channels = group :: groups.Channels }
                        else
                            { groups with
                                Playlists = group :: groups.Playlists }

                    Some updated
                | None -> None

            { model with ByPlaylist = updated }, Cmd.none

        | LooseFilesLoaded files ->
            let updated =
                match model.ByPlaylist with
                | Some groups -> { groups with LooseFiles = Some files } |> Some
                | None -> None

            { model with ByPlaylist = updated }, Cmd.none

        | RemoveFiles files ->
            for file in files do
                file.Delete()

            let updated =
                { model with
                    ByLastAccess =
                        match model.ByLastAccess with
                        | Some la -> la.Remove(files) |> List.ofSeq |> Some
                        | None -> None
                    ByPlaylist =
                        match model.ByPlaylist with
                        | Some bpl ->
                            { bpl with
                                ScopeSearches = bpl.ScopeSearches.Remove(files)
                                Channels = bpl.Channels.Remove(files) |> List.ofSeq
                                Playlists = bpl.Playlists.Remove(files) |> List.ofSeq
                                LooseFiles =
                                    match bpl.LooseFiles with
                                    | Some l -> l.Remove(files) |> Some
                                    | None -> None }
                            |> Some
                        | None -> None }

            updated, Cmd.none

        | RemoveByLastAccess group ->
            { model with
                ByLastAccess = model.ByLastAccess.Value |> List.except ([ group ]) |> Some },
            group.GetFiles() |> Array.ofSeq |> RemoveFiles |> Cmd.ofMsg

        | RemoveFromLastAccessGroup(group, files) ->
            { model with
                ByLastAccess =
                    model.ByLastAccess.Value
                    |> List.map (fun g -> if g = group then g.Remove(files) else g)
                    |> Some },
            RemoveFiles files |> Cmd.ofMsg

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

        | OpenFile file ->
            ShellCommands.OpenFile file.FullName
            model, Cmd.none

    let private header text = TextBlock(text).header ()

    let private displayWithMb label (bytes: int64) =
        let mbs = double bytes / 1024. / 1024.
        TextBlock($"{label} ({mbs:f2} Mb)")

    let private deletableFile label (file: FileInfo) labelFirst =
        (HStack(5) {
            let name =
                (displayWithMb $"{label}" file.Length).tappable(OpenFile file, "open this file").centerVertical ()

            let button = Button(Icon.trash, RemoveFiles [| file |]).tooltip ("delete this file")

            if labelFirst then
                name
                button
            else
                button
                name
        })

    let private deletableFileFirst label (file: FileInfo) =
        deletableFile label (file: FileInfo) true

    let private deletableFileLast label (file: FileInfo) =
        deletableFile label (file: FileInfo) false

    let private reportFiles label (msg: Msg) (files: FileInfo array) =
        (HStack(5) {
            let bytes = (files |> Array.map _.Length |> Array.sum)
            (displayWithMb $"{files.Length} {label}" bytes).centerVertical ()
            Button(Icon.trash, msg).tooltip ("clear these files")
        })
            .right()
            .isVisible (files.Length > 0)

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

    let private scopeSearch prefix (file: FileInfo) =
        let label =
            file.Name.StripAffixes(prefix + Youtube.SearchAffix, JsonFileDataStore.FileExtension)

        (deletableFileLast label file).card().margin (5)

    let private displaySearches icon title prefix (files: FileInfo array) =
        HWrap() {
            if Array.isEmpty files |> not then
                header (icon + title + Icon.scopeSearch)

                ItemsControl(files, fun file -> scopeSearch prefix file).itemsPanel (HWrapEmpty())
        }

    let private byPlaylist model =
        VStack() {
            let report label files =
                reportFiles label (RemoveFiles files) files

            let scopeSearches, channels, playlists, looseFiles =
                match model.ByPlaylist with
                | Some g -> Some g.ScopeSearches, g.Channels, g.Playlists, g.LooseFiles
                | None -> None, [], [], None

            let displayPlaylistGroup (group: PlaylistGroup) =
                (VStack() {
                    let icon = ScopeViews.getIcon (group.Scope.GetType())
                    header (icon + group.Playlist.Title)

                    (deletableFileFirst ("info and contents" + Icon.playlistLike) group.File).right ()

                    if group.Thumbnail <> null then
                        (deletableFileFirst ("thumbnail" + Icon.thumbnail) group.Thumbnail).right ()

                    report ("indexes" + Icon.index) group.Indexes
                    report ("video caches" + Icon.videoCache) group.Videos
                    report ("video thumbnails" + Icon.thumbnail) group.VideoThumbnails
                })
                    .card()
                    .margin (10)

            ItemsControl(channels, displayPlaylistGroup).itemsPanel (HWrapEmpty())
            ItemsControl(playlists, displayPlaylistGroup).itemsPanel (HWrapEmpty())

            match looseFiles with
            | Some files ->
                (VStack() {
                    header (Icon.video + "Videos")
                    report ("caches" + Icon.videoCache) files.Videos
                    report ("indexes" + Icon.index) files.VideoIndexes
                    report ("thumbnails" + Icon.thumbnail) files.Thumbnails
                })
                    .card()
                    .right ()

                header ("Other / loose files")

                ItemsControl(files.Other, fun f -> (deletableFileLast f.Name f).card().margin (5))
                    .itemsPanel (HWrapEmpty())
            | None -> header "loading..."

            if scopeSearches.IsSome then
                displaySearches
                    Icon.channel
                    "Channel searches"
                    ChannelScope.StorageKeyPrefix
                    scopeSearches.Value.Channels

                displaySearches
                    Icon.playlist
                    "Playlist searches"
                    PlaylistScope.StorageKeyPrefix
                    scopeSearches.Value.Playlists

                displaySearches Icon.video "Video searches" Video.StorageKeyPrefix scopeSearches.Value.Videos
        }

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

                Expander($"Files by {Icon.channel}channel, {Icon.playlist}playlist and type", byPlaylist model)
                    .onExpanding(fun _ -> ExpandingByPlaylist)
                    .isExpanded(model.ByPlaylist.IsSome)
                    .gridRow(4)
                    .gridColumnSpan (3)
            }
        )
