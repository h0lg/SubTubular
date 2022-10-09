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

        internal async Task Process()
        {
            var cacheFolder = Folder.GetPath(Folders.cache);
            var indexRepo = new VideoIndexRepository(cacheFolder);
            var dataStore = new JsonFileDataStore(cacheFolder);

            switch (Scope)
            {
                case ClearCache.Scopes.all: dataStore.Clear(NotAccessedForDays); break;
                case ClearCache.Scopes.videos:
                    if (Ids.HasAny())
                        foreach (var videoId in Ids.Select(v => VideoId.Parse(v).Value))
                        {
                            indexRepo.Delete(videoId);
                            dataStore.Delete(videoId);
                        }
                    else
                    {
                        indexRepo.Delete(IsVideoKey, NotAccessedForDays);
                        dataStore.Delete(IsVideoKey, NotAccessedForDays);
                    }

                    break;
                case ClearCache.Scopes.playlists:
                    await ClearPlaylists(SearchPlaylist.StorageKeyPrefix, v => PlaylistId.Parse(v).Value);
                    break;
                case ClearCache.Scopes.channels:
                    await ClearPlaylists(SearchChannel.StorageKeyPrefix, v => ChannelId.Parse(v).Value);
                    break;
                case ClearCache.Scopes.users:
                    await ClearPlaylists(SearchUser.StorageKeyPrefix, v => UserName.Parse(v).Value);
                    break;
                default: throw new NotImplementedException($"Clearing {scope} {Scope} is not implemented.");
            }

            bool IsVideoKey(string key) => !(key.StartsWith(SearchPlaylist.StorageKeyPrefix)
                || key.StartsWith(SearchChannel.StorageKeyPrefix)
                || key.StartsWith(SearchUser.StorageKeyPrefix));

            async Task ClearPlaylists(string keyPrefix, Func<string, string> parseId)
            {
                var deletableKeys = Ids.HasAny() ? Ids.Select(v => keyPrefix + parseId(v)).ToArray()
                    : dataStore.GetKeysByPrefix(keyPrefix, NotAccessedForDays).ToArray();

                foreach (var key in deletableKeys)
                {
                    var playlist = await dataStore.GetAsync<Playlist>(key);

                    foreach (var video in playlist.Videos)
                    {
                        dataStore.Delete(video.Key);
                        indexRepo.Delete(video.Key);
                    }

                    dataStore.Delete(key);
                    indexRepo.Delete(key);
                }
            }
        }

        internal enum Scopes { all, videos, playlists, channels, users }
    }
}