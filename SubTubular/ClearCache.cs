using SubTubular.Extensions;
using YoutubeExplode.Channels;

namespace SubTubular;

public sealed class ClearCache
{
    public Scopes Scope { get; set; }
    public IEnumerable<string>? Aliases { get; set; }
    public ushort? NotAccessedForDays { get; set; }
    public Modes Mode { get; set; }

    public enum Scopes { all, videos, playlists, channels }
    public enum Modes { summary, verbose, simulate }
}

public struct LastAccessGroup(string timeSpanLabel,
    FileInfo[] searches, FileInfo[] playlistLikes, FileInfo[] indexes, FileInfo[] videos, FileInfo[] thumbnails)
{
    public string TimeSpanLabel { get; } = timeSpanLabel;
    public FileInfo[] Searches { get; } = searches;
    public FileInfo[] PlaylistLikes { get; } = playlistLikes;
    public FileInfo[] Indexes { get; } = indexes;
    public FileInfo[] Videos { get; } = videos;
    public FileInfo[] Thumbnails { get; } = thumbnails;

    public IEnumerable<FileInfo> GetFiles()
    {
        foreach (var s in Searches) yield return s;
        foreach (var p in PlaylistLikes) yield return p;
        foreach (var i in Indexes) yield return i;
        foreach (var v in Videos) yield return v;
        foreach (var t in Thumbnails) yield return t;
    }

    public LastAccessGroup Remove(FileInfo[] files)
        => new(TimeSpanLabel,
            Searches.Except(files).ToArray(),
            PlaylistLikes.Except(files).ToArray(),
            Indexes.Except(files).ToArray(),
            Videos.Except(files).ToArray(),
            Thumbnails.Except(files).ToArray());
}

public struct ScopeSearches(FileInfo[] channels, FileInfo[] playlists, FileInfo[] videos)
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

public struct PlaylistGroup(PlaylistLikeScope scope, Playlist playlist, FileInfo file, FileInfo? thumbnail,
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

public static class CacheManager
{
    public static LastAccessGroup[] LoadByLastAccess(string cacheFolder)
    {
        var now = DateTime.Now;

        return FileHelper
            .EnumerateFiles(cacheFolder, "*", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastAccessTime) // latest first
            .GroupBy(f => DescribeTimeSpan(now - f.LastAccessTime))
            .Select(group =>
            {
                var searches = group.ToArray().GetSearches().GetFiles().ToArray();
                var json = group.WithExtension(JsonFileDataStore.FileExtension).Except(searches).ToArray();
                var videos = json.WithPrefix(Video.StorageKeyPrefix).ToArray();
                var playlistLikes = json.Except(videos).ToArray();
                var indexes = group.WithExtension(VideoIndexRepository.FileExtension).ToArray();
                var thumbnails = group.WithExtension(string.Empty).ToArray();

                return new LastAccessGroup(group.Key, searches: searches,
                    playlistLikes: playlistLikes, indexes: indexes, videos: videos, thumbnails: thumbnails);
            })
            .ToArray();
    }

    // Function to describe a TimeSpan into specific ranges
    private static string DescribeTimeSpan(TimeSpan ts)
    {
        if (ts < TimeSpan.FromDays(1.0)) return "day";
        if (ts < TimeSpan.FromDays(7.0)) return $"{ts.Days + 1} days";

        if (ts < TimeSpan.FromDays(30.0))
        {
            var weeks = ts.Days / 7;
            return weeks == 0 ? "week" : $"{weeks + 1} weeks";
        }

        if (ts < TimeSpan.FromDays(90)) // 90 days for roughly 3 months (quarter year)
        {
            var months = ts.Days / 30;
            return months == 0 ? "month" : $"{months + 1} months";
        }

        return "eon";
    }

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
                        if (task.Exception != null) dispatchException(task.Exception);
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

    public static async Task<(IEnumerable<string> cachesDeleted, IEnumerable<string> indexesDeleted)> Clear(
        ClearCache command, DataStore cacheDataStore, VideoIndexRepository videoIndexRepo)
    {
        List<string> cachesDeleted = [], indexesDeleted = [];
        var simulate = command.Mode == ClearCache.Modes.simulate;

        switch (command.Scope)
        {
            case ClearCache.Scopes.all:
                indexesDeleted.AddRange(videoIndexRepo.Delete(notAccessedForDays: command.NotAccessedForDays, simulate: simulate));
                cachesDeleted.AddRange(cacheDataStore.Delete(notAccessedForDays: command.NotAccessedForDays, simulate: simulate));

                break;
            case ClearCache.Scopes.videos:
                if (command.Aliases.HasAny())
                {
                    var parsed = command.Aliases!.ToDictionary(id => id, VideosScope.TryParseId);
                    var invalid = parsed.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();

                    if (invalid.Length > 0) throw new InputException(
                        "The following inputs are not valid video IDs or URLs: " + invalid.Join(" "));

                    DeleteByNames(parsed.Values.Select(videoId => Video.StorageKeyPrefix + videoId!));
                }
                else
                {
                    cachesDeleted.AddRange(cacheDataStore.Delete(keyPrefix: Video.StorageKeyPrefix,
                       notAccessedForDays: command.NotAccessedForDays, simulate: simulate));

                    indexesDeleted.AddRange(videoIndexRepo.Delete(Video.StorageKeyPrefix,
                       notAccessedForDays: command.NotAccessedForDays, simulate: simulate));
                }
                break;
            case ClearCache.Scopes.playlists:
                await ClearPlaylists(PlaylistScope.StorageKeyPrefix, cacheDataStore,
                    v =>
                    {
                        var id = PlaylistScope.TryParseId(v);
                        return id == null ? [] : [id];
                    });

                break;
            case ClearCache.Scopes.channels:
                Func<string, string[]?>? parseAlias = null;

                if (command.Aliases.HasAny())
                {
                    var aliasToChannelIds = await ClearChannelAliases(command.Aliases!, cacheDataStore, simulate);
                    parseAlias = alias => aliasToChannelIds.TryGetValue(alias, out var channelIds) ? channelIds : null;
                }
                else DeleteByName(ChannelAliasMap.StorageKey);

                await ClearPlaylists(ChannelScope.StorageKeyPrefix, cacheDataStore, parseAlias!);
                break;
            default: throw new NotImplementedException($"Clearing {nameof(ClearCache.Scope)} {command.Scope} is not implemented.");
        }

        return (cachesDeleted, indexesDeleted);

        void DeleteByName(string name)
        {
            cachesDeleted.AddRange(cacheDataStore.Delete(key: name, simulate: simulate));
            indexesDeleted.AddRange(videoIndexRepo.Delete(keyPrefix: name, simulate: simulate));
        }

        void DeleteByNames(IEnumerable<string> names)
        {
            foreach (var name in names) DeleteByName(name);
        }

        async Task ClearPlaylists(string keyPrefix, DataStore playListLikeDataStore, Func<string, string[]?> parseId)
        {
            string[] deletableKeys;

            if (command.Aliases.HasAny())
            {
                var parsed = command.Aliases!.ToDictionary(id => id, id => parseId(id));

                var invalid = parsed.Where(pair => !pair.Value.HasAny() || pair.Value!.All(id => id == null))
                    .Select(pair => pair.Key).ToArray();

                if (invalid.Length > 0) throw new InputException(
                    $"The following inputs are not valid {keyPrefix}IDs or URLs: " + invalid.Join(" "));

                deletableKeys = parsed.Values.SelectMany(ids => ids!).Where(id => id != null)
                    .Distinct().Select(id => keyPrefix + id).ToArray();
            }
            else deletableKeys = playListLikeDataStore.GetKeysByPrefix(keyPrefix, command.NotAccessedForDays).ToArray();

            foreach (var key in deletableKeys)
            {
                var playlist = await playListLikeDataStore.GetAsync<Playlist>(key);
                if (playlist != null) DeleteByNames(playlist.GetVideoIds().Select(videoId => Video.StorageKeyPrefix + videoId));
                DeleteByName(key);
            }
        }
    }

    private static async Task<Dictionary<string, string[]>> ClearChannelAliases(
        IEnumerable<string> aliases, DataStore dataStore, bool simulate)
    {
        var cachedMaps = await ChannelAliasMap.LoadListAsync(dataStore);
        var matchedMaps = new List<ChannelAliasMap>();

        var aliasToChannelIds = aliases.ToDictionary(alias => alias, alias =>
        {
            var valid = Prevalidate.ChannelAlias(alias);
            var matching = valid.Select(alias => cachedMaps.ForAlias(alias)).WithValue().ToArray();

            matchedMaps.AddRange(matching);

            return matching.Select(map => map.ChannelId)
                // append incoming ChannelId even if it isn't included in cached idMaps
                .Append(valid.SingleOrDefault(alias => alias is ChannelId)?.ToString())
                .Distinct().WithValue().ToArray();
        });

        if (!simulate)
        {
            var channelIds = aliasToChannelIds.SelectMany(pair => pair.Value).Distinct().ToArray();
            var siblings = cachedMaps.Where(map => channelIds.Contains(map.ChannelId));
            var removable = matchedMaps.Union(siblings).ToArray();
            if (removable.Length > 0) await ChannelAliasMap.RemoveEntriesAsync(removable, dataStore);
        }

        return aliasToChannelIds;
    }

    private static (FileInfo[] jsonCaches, FileInfo[] indexes) PartitionByExtension(this IEnumerable<FileInfo> files)
    {
        var groups = files.GroupBy(f => f.Extension).ToArray();
        var jsonCaches = groups.SingleOrDefault(g => g.Key == JsonFileDataStore.FileExtension)?.ToArray() ?? [];
        var indexes = groups.SingleOrDefault(g => g.Key == VideoIndexRepository.FileExtension)?.ToArray() ?? [];
        return (jsonCaches, indexes);
    }

    private static ScopeSearches GetSearches(this FileInfo[] files) => new(
        channels: files.GetSearches(ChannelScope.StorageKeyPrefix),
        playlists: files.GetSearches(PlaylistScope.StorageKeyPrefix),
        videos: files.GetSearches(Video.StorageKeyPrefix));

    private static FileInfo[] GetSearches(this FileInfo[] files, string storageKeyPrefix)
        => files.WithPrefix(storageKeyPrefix + Youtube.SearchAffix).ToArray();

    private static bool HasPrefix(this FileInfo file, string prefix) => file.Name.StartsWith(prefix);

    private static IEnumerable<FileInfo> WithPrefix(this IEnumerable<FileInfo> files, string prefix)
        => files.Where(f => f.HasPrefix(prefix));

    private static IEnumerable<FileInfo> WithExtension(this IEnumerable<FileInfo> files, string extension)
        => files.Where(f => f.Extension == extension);

    public static IEnumerable<LastAccessGroup> Remove(this IEnumerable<LastAccessGroup> groups, FileInfo[] files)
        => groups.Select(g => g.Remove(files)).Where(g => g.GetFiles().Count() > 0);

    public static IEnumerable<PlaylistGroup> Remove(this IEnumerable<PlaylistGroup> groups, FileInfo[] files)
        => groups.Select(g => g.Remove(files)).Where(g => !files.Contains(g.File));
}
