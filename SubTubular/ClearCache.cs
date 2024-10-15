using SubTubular.Extensions;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

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
                    var parsed = command.Aliases!.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"')));
                    var invalid = parsed.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();

                    if (invalid.Length > 0) throw new InputException(
                        "The following inputs are not valid video IDs or URLs: " + invalid.Join(" "));

                    DeleteByNames(parsed.Values.Select(videoId => Video.StorageKeyPrefix + videoId!.Value));
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
                        var id = PlaylistId.TryParse(v);
                        return id.HasValue ? [id] : [];
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
}
