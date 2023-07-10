using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace SubTubular
{
    [Verb(Command, aliases: new[] { "clear" },
        HelpText = "Deletes cached metadata and full-text indexes for "
            + $"{nameof(Scopes.channels)}, {nameof(Scopes.playlists)} and {nameof(Scopes.videos)}.")]
    internal sealed class ClearCache
    {
        internal const string Command = "clear-cache";
        private const string scope = "scope", ids = "ids";

        [Value(0, MetaName = scope, Required = true, HelpText = "The type of caches to delete."
            + $" For {nameof(Scopes.playlists)} and {nameof(Scopes.channels)}"
            + $" this will include the associated {nameof(Scopes.videos)}.")]
        public Scopes Scope { get; set; }

        [Value(1, MetaName = ids, HelpText = $"The space-separated IDs or URLs of elements in the '{scope}' to delete caches for."
            + $" Can be used with every '{scope}' but '{nameof(Scopes.all)}'"
            + $" while supporting user names, channel handles and slugs besides IDs for '{nameof(Scopes.channels)}'."
            + $" If not set, all elements in the specified '{scope}' are considered for deletion."
            + SearchVideos.QuoteIdsStartingWithDash)]
        public IEnumerable<string> Ids { get; set; }

        [Option('l', "last-access",
            HelpText = "The maximum number of days since the last access of a cache file for it to be excluded from deletion."
                + " Effectively only deletes old caches that haven't been accessed for this number of days."
                + $" Ignored for explicitly set '{ids}'.")]
        public ushort? NotAccessedForDays { get; set; }

        [Option('m', "mode", Default = Modes.summary,
            HelpText = "The deletion mode;"
                + $" '{nameof(Modes.summary)}' only outputs how many of what file type were deleted."
                + $" '{nameof(Modes.verbose)}' outputs the deleted file names as well as the summary."
                + $" '{nameof(Modes.simulate)}' lists all file names that would be deleted by running the command instead of deleting them."
                + " You can use this to preview the files that would be deleted.")]
        public Modes Mode { get; set; }

        internal async Task<(IEnumerable<string>, IEnumerable<string>)> Process()
        {
            var filesDeleted = new List<string>();
            var cacheFolder = Folder.GetPath(Folders.cache);
            var simulate = Mode == Modes.simulate;

            switch (Scope)
            {
                case Scopes.all:
                    filesDeleted.AddRange(FileHelper.DeleteFiles(cacheFolder,
                        notAccessedForDays: NotAccessedForDays, simulate: simulate));

                    break;
                case Scopes.videos:
                    if (Ids.HasAny())
                    {
                        var parsed = Ids.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"')));
                        var invalid = parsed.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();

                        if (invalid.Length > 0) throw new InputException(
                            "The following inputs are not valid video IDs or URLs: " + invalid.Join(" "));

                        DeleteFilesByNames(parsed.Values.Select(videoId => Video.StorageKeyPrefix + videoId.Value));
                    }
                    else filesDeleted.AddRange(FileHelper.DeleteFiles(cacheFolder, Video.StorageKeyPrefix + "*",
                        notAccessedForDays: NotAccessedForDays, simulate: simulate));

                    break;
                case Scopes.playlists:
                    await ClearPlaylists(SearchPlaylist.StorageKeyPrefix, new JsonFileDataStore(cacheFolder),
                        v => new[] { PlaylistId.TryParse(v)?.Value });

                    break;
                case Scopes.channels:
                    var dataStore = new JsonFileDataStore(cacheFolder);
                    Func<string, string[]> parseAlias = null;

                    if (Ids.HasAny())
                    {
                        var aliasToChannelIds = await ClearChannelAliases(Ids, dataStore, simulate);
                        parseAlias = alias => aliasToChannelIds.TryGetValue(alias, out var channelIds) ? channelIds : null;
                    }
                    else DeleteFileByName(ChannelAliasMap.StorageKey);

                    await ClearPlaylists(SearchChannel.StorageKeyPrefix, dataStore, parseAlias);
                    break;
                default: throw new NotImplementedException($"Clearing {scope} {Scope} is not implemented.");
            }

            return (filesDeleted.Where(fileName => fileName.EndsWith(JsonFileDataStore.FileExtension)),
                filesDeleted.Where(fileName => fileName.EndsWith(VideoIndexRepository.FileExtension)));

            void DeleteFileByName(string name) => filesDeleted.AddRange(
                FileHelper.DeleteFiles(cacheFolder, name + ".*", simulate: simulate));

            void DeleteFilesByNames(IEnumerable<string> names) { foreach (var name in names) DeleteFileByName(name); }

            async Task ClearPlaylists(string keyPrefix, JsonFileDataStore dataStore, Func<string, string[]> parseId)
            {
                string[] deletableKeys;

                if (Ids.HasAny())
                {
                    var parsed = Ids.ToDictionary(id => id, id => parseId(id));

                    var invalid = parsed.Where(pair => !pair.Value.HasAny() || pair.Value.All(id => id == null))
                        .Select(pair => pair.Key).ToArray();

                    if (invalid.Length > 0) throw new InputException(
                        $"The following inputs are not valid {keyPrefix}IDs or URLs: " + invalid.Join(" "));

                    deletableKeys = parsed.Values.SelectMany(ids => ids).Where(id => id != null)
                        .Distinct().Select(id => keyPrefix + id).ToArray();
                }
                else deletableKeys = dataStore.GetKeysByPrefix(keyPrefix, NotAccessedForDays).ToArray();

                foreach (var key in deletableKeys)
                {
                    var playlist = await dataStore.GetAsync<Playlist>(key);
                    if (playlist != null) DeleteFilesByNames(playlist.Videos.Keys.Select(videoId => Video.StorageKeyPrefix + videoId));
                    DeleteFileByName(key);
                }
            }
        }

        private static async Task<Dictionary<string, string[]>> ClearChannelAliases(
            IEnumerable<string> aliases, JsonFileDataStore dataStore, bool simulate)
        {
            var cachedMaps = await ChannelAliasMap.LoadList(dataStore);
            var matchedMaps = new List<ChannelAliasMap>();

            var aliasToChannelIds = aliases.ToDictionary(alias => alias, alias =>
            {
                var valid = SearchChannel.ValidateAlias(alias);
                var matching = valid.Select(alias => cachedMaps.ForAlias(alias)).Where(map => map != null).ToArray();
                matchedMaps.AddRange(matching);

                return matching.Select(map => map.ChannelId)
                    // append incoming ChannelId even if it isn't included in cached idMaps
                    .Append(valid.SingleOrDefault(alias => alias is ChannelId)?.ToString())
                    .Distinct().Where(id => id != null).ToArray();
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

        internal enum Scopes { all, videos, playlists, channels }
        internal enum Modes { summary, verbose, simulate }
    }
}