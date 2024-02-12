using SubTubular.Extensions;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace SubTubular;

public sealed class ClearCache
{
    public Scopes Scope { get; set; }
    public IEnumerable<string>? Ids { get; set; }
    public ushort? NotAccessedForDays { get; set; }
    public Modes Mode { get; set; }

    public enum Scopes { all, videos, playlists, channels }
    public enum Modes { summary, verbose, simulate }
}

public static class CacheClearer
{
    public static async Task<(IEnumerable<string> cachesDeleted, IEnumerable<string> indexesDeleted)> Process(ClearCache command, DataStore cacheDataStore)
    {
        var filesDeleted = new List<string>();
        var cacheFolder = Folder.GetPath(Folders.cache);
        var simulate = command.Mode == ClearCache.Modes.simulate;

        switch (command.Scope)
        {
            case ClearCache.Scopes.all:
                filesDeleted.AddRange(cacheDataStore.DeleteFiles(notAccessedForDays: command.NotAccessedForDays, simulate: simulate));

                break;
            case ClearCache.Scopes.videos:
                if (command.Ids.HasAny())
                {
                    var parsed = command.Ids!.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"')));
                    var invalid = parsed.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();

                    if (invalid.Length > 0) throw new InputException(
                        "The following inputs are not valid video IDs or URLs: " + invalid.Join(" "));

                    DeleteFilesByNames(parsed.Values.Select(videoId => Video.StorageKeyPrefix + videoId!.Value));
                }
                else filesDeleted.AddRange(cacheDataStore.DeleteFiles(Video.StorageKeyPrefix + "*",
                    notAccessedForDays: command.NotAccessedForDays, simulate: simulate));

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

                if (command.Ids.HasAny())
                {
                    var aliasToChannelIds = await ClearChannelAliases(command.Ids!, cacheDataStore, simulate);
                    parseAlias = alias => aliasToChannelIds.TryGetValue(alias, out var channelIds) ? channelIds : null;
                }
                else DeleteFileByName(ChannelAliasMap.StorageKey);

                await ClearPlaylists(ChannelScope.StorageKeyPrefix, cacheDataStore, parseAlias!);
                break;
            default: throw new NotImplementedException($"Clearing {nameof(ClearCache.Scope)} {command.Scope} is not implemented.");
        }

        return (filesDeleted.Where(fileName => fileName.EndsWith(cacheDataStore.FileExtension)),
            filesDeleted.Where(fileName => fileName.EndsWith(VideoIndexRepository.FileExtension)));

        void DeleteFileByName(string name) => filesDeleted.AddRange(
            cacheDataStore.DeleteFiles(name + ".*", simulate: simulate));

        void DeleteFilesByNames(IEnumerable<string> names) { foreach (var name in names) DeleteFileByName(name); }

        async Task ClearPlaylists(string keyPrefix, DataStore dataStore, Func<string, string[]?> parseId)
        {
            string[] deletableKeys;

            if (command.Ids.HasAny())
            {
                var parsed = command.Ids!.ToDictionary(id => id, id => parseId(id));

                var invalid = parsed.Where(pair => pair.Value == null || !pair.Value.HasAny() || pair.Value.All(id => id == null))
                    .Select(pair => pair.Key).ToArray();

                if (invalid.Length > 0) throw new InputException(
                    $"The following inputs are not valid {keyPrefix}IDs or URLs: " + invalid.Join(" "));

                deletableKeys = parsed.Values.SelectMany(ids => ids!).Where(id => id != null)
                    .Distinct().Select(id => keyPrefix + id).ToArray();
            }
            else deletableKeys = dataStore.GetKeysByPrefix(keyPrefix, command.NotAccessedForDays).ToArray();

            foreach (var key in deletableKeys)
            {
                var playlist = await dataStore.GetAsync<Playlist>(key);
                if (playlist != null) DeleteFilesByNames(playlist.Videos.Keys.Select(videoId => Video.StorageKeyPrefix + videoId));
                DeleteFileByName(key);
            }
        }
    }

    private static async Task<Dictionary<string, string[]>> ClearChannelAliases(
        IEnumerable<string> aliases, DataStore dataStore, bool simulate)
    {
        var cachedMaps = await ChannelAliasMap.LoadList(dataStore);
        var matchedMaps = new List<ChannelAliasMap>();

        var aliasToChannelIds = aliases.ToDictionary(alias => alias, alias =>
        {
            var valid = CommandValidator.ValidateChannelAlias(alias);
            var matching = valid.Select(alias => cachedMaps.ForAlias(alias))
                .Where(map => map != null).Cast<ChannelAliasMap>().ToArray();

            matchedMaps.AddRange(matching);

            return matching.Select(map => map.ChannelId)
                // append incoming ChannelId even if it isn't included in cached idMaps
                .Append(valid.SingleOrDefault(alias => alias is ChannelId)?.ToString())
                .Distinct().Where(id => id != null).Cast<string>().ToArray();
        });

        if (!simulate)
        {
            var channelIds = aliasToChannelIds.SelectMany(pair => pair.Value).Distinct().ToArray();
            var siblings = cachedMaps.Where(map => channelIds.Contains(map.ChannelId));
            var removable = matchedMaps.Union(siblings).ToArray();

            if (removable.Length > 0)
            {
                foreach (var map in removable) cachedMaps.Remove(map);
                await ChannelAliasMap.SaveList(cachedMaps, dataStore);
            }
        }

        return aliasToChannelIds;
    }
}
