using SubTubular.Extensions;
using YoutubeExplode.Channels;

namespace SubTubular;

public readonly struct ScopeSearches(FileInfo[] channels, FileInfo[] playlists, FileInfo[] videos)
{
    public FileInfo[] Channels { get; } = channels;
    public FileInfo[] Playlists { get; } = playlists;
    public FileInfo[] Videos { get; } = videos;

    internal IEnumerable<FileInfo> GetFiles()
    {
        foreach (var c in Channels) yield return c;
        foreach (var p in Playlists) yield return p;
        foreach (var v in Videos) yield return v;
    }

    public ScopeSearches Remove(FileInfo[] files)
        => new(Channels.Except(files).ToArray(),
            Playlists.Except(files).ToArray(),
            Videos.Except(files).ToArray());
}

public readonly struct PlaylistGroup(PlaylistLikeScope scope, Playlist playlist, FileInfo file, FileInfo? thumbnail,
    FileInfo[] indexes, FileInfo[] videos, FileInfo[] videoThumbnails)
{
    public PlaylistLikeScope Scope { get; } = scope;
    public Playlist Playlist { get; } = playlist;
    public FileInfo File { get; } = file;
    public FileInfo? Thumbnail { get; } = thumbnail;
    public FileInfo[] Indexes { get; } = indexes;
    public FileInfo[] Videos { get; } = videos;
    public FileInfo[] VideoThumbnails { get; } = videoThumbnails;

    internal IEnumerable<FileInfo> GetFiles()
    {
        yield return File;
        if (Thumbnail != null) yield return Thumbnail;
        foreach (var i in Indexes) yield return i;
        foreach (var v in Videos) yield return v;
        foreach (var t in VideoThumbnails) yield return t;
    }

    public PlaylistGroup Remove(FileInfo[] files)
        => new(Scope, Playlist, File,
            Thumbnail == null ? null : files.Contains(Thumbnail) ? null : Thumbnail,
            Indexes.Except(files).ToArray(),
            Videos.Except(files).ToArray(),
            VideoThumbnails.Except(files).ToArray());
}

public sealed record LooseFiles
{
    public FileInfo[] Videos { get; init; }
    public FileInfo[] VideoIndexes { get; init; }
    public FileInfo[] Thumbnails { get; init; }
    public FileInfo[] Other { get; init; }

    public LooseFiles(FileInfo[] videos, FileInfo[] videoIndexes, FileInfo[] thumbnails, FileInfo[] other)
    {
        Videos = videos;
        VideoIndexes = videoIndexes;
        Thumbnails = thumbnails;
        Other = other;
    }

    public LooseFiles Remove(FileInfo[] files)
        => new(Videos.Except(files).ToArray(), VideoIndexes.Except(files).ToArray(),
            Thumbnails.Except(files).ToArray(), Other.Except(files).ToArray());
}

static partial class CacheManager
{
    public static (ScopeSearches, Func<Action<PlaylistGroup>, Action<LooseFiles>, Action<Exception>, Task> processAsync)
        LoadByPlaylist(string cacheFolder, Youtube youtube, Func<string, string> getThumbnailFileName)
    {
        var files = FileHelper.EnumerateFiles(cacheFolder, "*", SearchOption.AllDirectories).ToArray();
        ScopeSearches searches = files.GetSearches();

        Func<Action<PlaylistGroup>, Action<LooseFiles>, Action<Exception>, Task> processAsync = new((dispatchGroup, dispatchLooseFiles, dispatchException)
            => Task.Run(async () =>
            {
                var channels = GetPlaylistLike(files, searches.Channels, ChannelScope.StorageKeyPrefix,
                    id => Youtube.GetChannelUrl((ChannelId)id),
                    id => new ChannelScope(id, 0, 0, 0),
                    scope => youtube.GetPlaylistAsync(scope, CancellationToken.None),
                    getThumbnailFileName);

                var playlists = GetPlaylistLike(files, searches.Playlists, PlaylistScope.StorageKeyPrefix,
                    Youtube.GetPlaylistUrl, id => new PlaylistScope(id, 0, 0, 0),
                    scope => youtube.GetPlaylistAsync(scope, CancellationToken.None), getThumbnailFileName);

                var tasks = channels.Concat(playlists).ToArray();

                try
                {
                    await foreach (var task in Task.WhenEach(tasks))
                    {
                        if (task.IsCompletedSuccessfully && task.Result.HasValue) dispatchGroup(task.Result.Value);
                        if (task.IsFaulted) dispatchException(task.Exception);
                    }
                }
                catch (Exception ex) { dispatchException(ex); }

                var looseFiles = files.Except(searches.GetFiles())
                    .Except(tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).WithValue().SelectMany(g => g.GetFiles()))
                    .ToArray();

                var looseThumbs = looseFiles.WithExtension(string.Empty).ToArray();
                var (looseVideos, looseVideoIndexes) = looseFiles.WithPrefix(Video.StorageKeyPrefix).PartitionByExtension();
                var other = looseFiles.Except(looseThumbs).Except(looseVideos).Except(looseVideoIndexes).ToArray();
                dispatchLooseFiles(new LooseFiles(videos: looseVideos, videoIndexes: looseVideoIndexes, thumbnails: looseThumbs, other: other));
            }));

        return (searches, processAsync);
    }

    public static IEnumerable<PlaylistGroup> Remove(this IEnumerable<PlaylistGroup> groups, FileInfo[] files)
        => groups.Select(g => g.Remove(files)).Where(g => !files.Contains(g.File));

    private static Task<PlaylistGroup?>[] GetPlaylistLike<Scope>(
        FileInfo[] files, FileInfo[] searches, string prefix, Func<string, string> getUrl, Func<string, Scope> createScope,
        Func<Scope, Task<Playlist>> getPlaylist, Func<string, string> getThumbnailFileName) where Scope : PlaylistLikeScope
    {
        var (caches, allIndexes) = files.WithPrefix(prefix).PartitionByExtension();
        return caches.Except(searches).Select(GroupForPlaylistLike).ToArray();

        async Task<PlaylistGroup?> GroupForPlaylistLike(FileInfo file)
        {
            var id = file.Name.StripAffixes(prefix, JsonFileDataStore.FileExtension);
            var scope = createScope(id);
            scope.AddPrevalidated(id, getUrl(id));
            var playlist = await getPlaylist(scope);
            var indexes = allIndexes.WithPrefix(prefix + id).ToArray();

            var thumbName = getThumbnailFileName(playlist.ThumbnailUrl);
            var thumbnail = files.SingleOrDefault(i => i.Name == thumbName);

            var videoIds = playlist.GetVideos().Ids().ToArray();
            var videoNames = videoIds.Select(id => Video.StorageKeyPrefix + id).ToArray();
            var videos = files.Where(f => videoNames.Any(n => f.HasPrefix(n))).ToArray();

            var videoThumbNames = videoIds.Select(id => getThumbnailFileName(Video.GuessThumbnailUrl(id))).ToArray();
            var videoThumbs = files.Where(f => videoThumbNames.Contains(f.Name)).ToArray();

            return new PlaylistGroup(scope, playlist, file, thumbnail, indexes, videos, videoThumbs);
        }
    }

    private static (FileInfo[] jsonCaches, FileInfo[] indexes) PartitionByExtension(this IEnumerable<FileInfo> files)
    {
        var groups = files.GroupBy(f => f.Extension).ToArray();
        var jsonCaches = groups.SingleOrDefault(g => g.Key == JsonFileDataStore.FileExtension)?.ToArray() ?? [];
        var indexes = groups.SingleOrDefault(g => g.Key == VideoIndexRepository.FileExtension)?.ToArray() ?? [];
        return (jsonCaches, indexes);
    }
}
