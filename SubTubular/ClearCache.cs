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

public static class CacheClearer
{
    public static async Task<(IEnumerable<string> cachesDeleted, IEnumerable<string> indexesDeleted)> Process(
        ClearCache command, DataStore cacheDataStore, VideoIndexRepository videoIndexRepo)
    {
        List<string> cachesDeleted = new(), indexesDeleted = new();
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
            indexesDeleted.AddRange(videoIndexRepo.Delete(key: name, simulate: simulate));
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
                if (playlist != null) DeleteByNames(playlist.Videos.Keys.Select(videoId => Video.StorageKeyPrefix + videoId));
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
}
