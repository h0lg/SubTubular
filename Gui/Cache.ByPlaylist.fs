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
    type Msg =
        | RemoveFiles of FileInfo array
        | OpenFile of FileInfo

    let displayWithMb label (bytes: int64) =
        let mbs = double bytes / 1024. / 1024.
        TextBlock($"{label} ({mbs:f2} Mb)")

    let reportFiles label (msg: 'msg) (files: FileInfo array) =
        (HStack(5) {
            let bytes = (files |> Array.map _.Length |> Array.sum)
            (displayWithMb $"{files.Length} {label}" bytes).centerVertical ()
            Button(Icon.trash, msg).tooltip ("clear these files")
        })
            .right()
            .isVisible (files.Length > 0)

module CacheByPlaylist =
    open Cache

    type ThumbnailCache(folder: string) =
        inherit DiskCachedWebImageLoader(folder)
        static member getFileName url = DiskCachedWebImageLoader.CreateMD5 url

    type Model =
        { ScopeSearches: ScopeSearches
          Channels: PlaylistGroup list
          Playlists: PlaylistGroup list
          LooseFiles: LooseFiles option }

    type Msg =
        | Loaded of Model
        | PlaylistGroupLoaded of PlaylistGroup
        | LooseFilesLoaded of LooseFiles
        | CacheMsg of Cache.Msg

    let load () =
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

                groups |> Loaded |> dispatch

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

    let update msg model =
        match msg with
        | Loaded _ -> model
        | CacheMsg _ -> model

        | PlaylistGroupLoaded group ->
            let updated =
                if group.Playlist.Channel = null then
                    { model with
                        Channels = group :: model.Channels }
                else
                    { model with
                        Playlists = group :: model.Playlists }

            updated

        | LooseFilesLoaded files -> { model with LooseFiles = Some files }

    let remove model files =
        { model with
            ScopeSearches = model.ScopeSearches.Remove(files)
            Channels = model.Channels.Remove(files) |> List.ofSeq
            Playlists = model.Playlists.Remove(files) |> List.ofSeq
            LooseFiles =
                match model.LooseFiles with
                | Some l -> l.Remove(files) |> Some
                | None -> None }

    let private header text = TextBlock(text).header ()

    let private deletableFile label (file: FileInfo) labelFirst =
        (HStack(5) {
            let name =
                (displayWithMb $"{label}" file.Length)
                    .tappable(OpenFile file |> CacheMsg, "open this file")
                    .centerVertical ()

            let button =
                Button(Icon.trash, RemoveFiles [| file |] |> CacheMsg).tooltip ("delete this file")

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

    let view model =
        VStack() {
            let report label files =
                reportFiles label (RemoveFiles files |> CacheMsg) files

            let scopeSearches, channels, playlists, looseFiles =
                match model with
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
