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
    [Verb("clear-cache", aliases: new[] { "clear" },
        HelpText = "Deletes cached metadata and full-text indexes for "
            + $"{nameof(Scopes.users)}, {nameof(Scopes.channels)}, {nameof(Scopes.playlists)} and {nameof(Scopes.videos)}.")]
    internal sealed class ClearCache
    {
        private const string scope = "scope", ids = "ids";

        [Value(0, MetaName = scope, Required = true, HelpText = "The type of caches to delete."
            + $" For {nameof(Scopes.playlists)}, {nameof(Scopes.channels)} and {nameof(Scopes.users)}"
            + $" this will include the associated {nameof(Scopes.videos)}.")]
        public Scopes Scope { get; set; }

        [Value(1, MetaName = ids, HelpText = $"The space-separated IDs or URLs of elements in the '{scope}' to delete caches for."
            + $" Can be used with every '{scope}' but '{nameof(Scopes.all)}', supplying the names instead of IDs for '{nameof(Scopes.users)}'."
            + $" If not set, all elements in the specified '{scope}' are considered for deletion.")]
        public IEnumerable<string> Ids { get; set; }

        [Option('l', "last-access",
            HelpText = "The maximum number of days since the last access of a cache file for it to be excluded from deletion."
                + " Effectively only deletes old caches that haven't been accessed for this number of days."
                + $" Ignored for explicitly set '{ids}'.")]
        public ushort? NotAccessedForDays { get; set; }

        internal async Task<(IEnumerable<string>, IEnumerable<string>)> Process()
        {
            var filesDeleted = new List<string>();
            var cacheFolder = Folder.GetPath(Folders.cache);

            switch (Scope)
            {
                case ClearCache.Scopes.all:
                    filesDeleted.AddRange(FileHelper.DeleteFiles(cacheFolder, notAccessedForDays: NotAccessedForDays));
                    break;
                case ClearCache.Scopes.videos:
                    if (Ids.HasAny())
                    {
                        var parsed = Ids.ToDictionary(id => id, id => VideoId.TryParse(id));
                        var valid = parsed.Where(pair => pair.Value != null);
                        DeleteFilesByNames(valid.Select(pair => pair.Value.Value.ToString()));
                    }
                    else filesDeleted.AddRange(FileHelper.DeleteFiles(cacheFolder,
                        notAccessedForDays: NotAccessedForDays, isFileNameDeletable: IsVideoFile));

                    break;
                case ClearCache.Scopes.playlists:
                    await ClearPlaylists(SearchPlaylist.StorageKeyPrefix, v => PlaylistId.TryParse(v)?.Value);
                    break;
                case ClearCache.Scopes.channels:
                    await ClearPlaylists(SearchChannel.StorageKeyPrefix, v => ChannelId.TryParse(v)?.Value);
                    break;
                case ClearCache.Scopes.users:
                    await ClearPlaylists(SearchUser.StorageKeyPrefix, v => UserName.TryParse(v)?.Value);
                    break;
                default: throw new NotImplementedException($"Clearing {scope} {Scope} is not implemented.");
            }

            return (filesDeleted.Where(fileName => fileName.EndsWith(JsonFileDataStore.FileExtension)),
                filesDeleted.Where(fileName => fileName.EndsWith(VideoIndexRepository.FileExtension)));

            bool IsVideoFile(string fileName) => !(fileName.StartsWith(SearchPlaylist.StorageKeyPrefix)
                || fileName.StartsWith(SearchChannel.StorageKeyPrefix)
                || fileName.StartsWith(SearchUser.StorageKeyPrefix));

            void DeleteFileByName(string name) => filesDeleted.AddRange(FileHelper.DeleteFiles(cacheFolder, name + ".*"));
            void DeleteFilesByNames(IEnumerable<string> names) { foreach (var name in names) DeleteFileByName(name); }

            async Task ClearPlaylists(string keyPrefix, Func<string, string> parseId)
            {
                var dataStore = new JsonFileDataStore(cacheFolder);

                var deletableKeys = Ids.HasAny()
                    ? Ids.Select(v => parseId(v)).Where(id => id != null).Select(id => keyPrefix + id).ToArray()
                    : dataStore.GetKeysByPrefix(keyPrefix, NotAccessedForDays).ToArray();

                foreach (var key in deletableKeys)
                {
                    var playlist = await dataStore.GetAsync<Playlist>(key);
                    DeleteFilesByNames(playlist.Videos.Keys);
                    DeleteFileByName(key);
                }
            }
        }

        internal enum Scopes { all, videos, playlists, channels, users }
    }
}