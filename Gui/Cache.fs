namespace SubTubular.Gui

open System
open System.IO
open System.Threading
open Fabulous
open Fabulous.Avalonia
open SubTubular
open AsyncImageLoader.Loaders

open type Fabulous.Avalonia.View

module Cache =
    type ThumbnailCache(folder: string) =
        inherit DiskCachedWebImageLoader(folder)
        static member getFileName url = DiskCachedWebImageLoader.CreateMD5 url

    type PlaylistGroup =
        { Playlist: Playlist
          File: FileInfo
          Thumbnail: FileInfo option
          Indexes: FileInfo list
          Videos: FileInfo list
          VideoThumbnails: FileInfo list }

    type GroupsByPlaylist =
        { Channels: PlaylistGroup list
          Playlists: PlaylistGroup list
          ChannelSearches: FileInfo list
          PlaylistSearches: FileInfo list
          Videos: FileInfo list }

    type FileType = { Label: string; Description: string }

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
        | RemoveByLastAccess of LastAccessGroup
        | RemoveFromLastAccessGroup of LastAccessGroup * FileInfo array
        | ExpandingFolders
        | OpenFolder of string

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

    let private getPlaylistLike<'Scope when 'Scope :> PlaylistLikeScope>
        (files: FileInfo list)
        (prefix: string)
        getUrl
        (createScope: string -> 'Scope)
        (getPlaylist: 'Scope -> Tasks.Task<Playlist>)
        =
        let data =
            files
            |> List.filter (fun f -> f.Name.StartsWith(prefix))
            |> List.groupBy (fun f -> f.Extension)

        let allIndexes =
            data |> List.find (fun g -> fst g = VideoIndexRepository.FileExtension) |> snd

        let caches =
            data |> List.find (fun g -> fst g = JsonFileDataStore.FileExtension) |> snd

        let searches =
            caches |> List.filter (fun f -> f.Name.StartsWith(prefix + "search "))

        let infos = caches |> List.except searches

        let getId (fileName: string) =
            let startIndex = prefix.Length
            let endIndex = fileName.Length - JsonFileDataStore.FileExtension.Length
            fileName.Substring(startIndex, endIndex - startIndex)

        searches,
        infos
        |> List.map (fun file ->
            task {
                let id = getId file.Name
                let scope = createScope id
                scope.AddPrevalidated(id, getUrl id)
                let! playlist = getPlaylist scope
                let indexName = prefix + id
                let indexes = allIndexes |> List.filter (fun i -> i.Name.StartsWith(indexName))
                let thumbName = ThumbnailCache.getFileName playlist.ThumbnailUrl
                let thumbnail = files |> List.tryFind (fun i -> i.Name = thumbName)
                let videoIds = playlist.GetVideos() |> Seq.map (fun v -> v.Id) |> Array.ofSeq
                let videoNames = videoIds |> Array.map (fun id -> Video.StorageKeyPrefix + id)

                let videos =
                    files
                    |> List.filter (fun f -> videoNames |> Array.exists (fun n -> f.Name.StartsWith(n)))

                let videoThumbNames =
                    videoIds
                    |> Array.map (fun id -> Video.GuessThumbnailUrl id |> ThumbnailCache.getFileName)

                let videoThumbs =
                    files |> List.filter (fun f -> videoThumbNames |> Array.contains f.Name)

                return
                    { Playlist = playlist
                      File = file
                      Indexes = indexes
                      Thumbnail = thumbnail
                      Videos = videos
                      VideoThumbnails = videoThumbs }
            })

    let private loadByPlaylist () =
        task {
            let files =
                FileHelper.EnumerateFiles(Services.CacheFolder, "*", SearchOption.AllDirectories)
                |> List.ofSeq

            let channels =
                getPlaylistLike
                    files
                    ChannelScope.StorageKeyPrefix
                    (fun id -> (YoutubeExplode.Channels.ChannelId) id |> Youtube.GetChannelUrl)
                    (fun id -> ChannelScope(id, 0us, 0us, 0f))
                    (fun scope -> Services.Youtube.GetPlaylistAsync(scope, CancellationToken.None))

            let playlists =
                getPlaylistLike
                    files
                    PlaylistScope.StorageKeyPrefix
                    (fun id -> (YoutubeExplode.Playlists.PlaylistId) id |> Youtube.GetPlaylistUrl)
                    (fun id -> PlaylistScope(id, 0us, 0us, 0f))
                    (fun scope -> Services.Youtube.GetPlaylistAsync(scope, CancellationToken.None))

            let! playlistLikes =
                [ channels; playlists ]
                |> List.map snd
                |> List.concat
                |> Tasks.Task.WhenAll
                |> Async.AwaitTask

            let inPlaylists = playlistLikes |> List.ofArray |> List.collect (fun g -> g.Videos)
            let looseVideos = files |> List.except inPlaylists

            return
                { Channels = snd channels |> List.map (fun t -> t.Result)
                  Playlists = snd playlists |> List.map (fun t -> t.Result)
                  ChannelSearches = fst channels
                  PlaylistSearches = fst playlists
                  Videos = looseVideos }
                |> ByPlaylistLoaded
        }

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
                loadByPlaylist () |> Cmd.OfTask.msg

        | ByPlaylistLoaded groups ->
            { model with
                ByPlaylist = groups |> Some },
            Cmd.none

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

                    Button(Icon.trash, RemoveByLastAccess group)
                        .tooltip("clear this data")
                        .fontSize(20)
                        .gridRow (1)

                    (VStack(5) {
                        let report label files = reportFiles label group files
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
                    .gridColumn (2)

                Expander("Files by type accessed within the last... " + Icon.recent, byLastAccess model)
                    .onExpanding(fun _ -> ExpandingByLastAccess)
                    .gridRow(2)
                    .gridColumnSpan (3)

                Expander(
                    "Files by playlist",
                    VStack() {
                        let channels, playlists, videos =
                            match model.ByPlaylist with
                            | Some groups -> groups.Channels, groups.Playlists, groups.Videos
                            | None -> [], [], []

                        ItemsControl(
                            channels,
                            fun channel ->
                                (Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Star ]) {
                                    TextBlock(channel.Playlist.Title).header ()

                                })
                                    .margin (10)
                        )

                        ItemsControl(
                            playlists,
                            fun playlist ->
                                (Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Star ]) {
                                    TextBlock(playlist.Playlist.Title).header ()
                                })
                                    .margin (10)
                        )
                    }
                )
                    .onExpanding(fun _ -> ExpandingByPlaylist)
                    .gridRow(4)
                    .gridColumnSpan (3)
            }
        )
