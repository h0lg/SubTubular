using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;

namespace SubTubular
{
    [Verb("search-channel", aliases: new[] { "channel", "c" },
        HelpText = "Searches the videos in a channel's Uploads playlist."
        + $" This is a glorified '{SearchPlaylist.Command}'.")]
    internal sealed class SearchChannel : SearchPlaylistCommand, RemoteValidated
    {
        internal const string StorageKeyPrefix = "channel ";

        [Value(0, MetaName = "channel", Required = true,
            HelpText = "The channel ID, handle, slug, user name or a URL for either of those.")]
        public string Alias { get; set; }

        internal override string Label => StorageKeyPrefix;

        #region VALIDATION
        private object[] validAliases;

        internal override void Validate()
        {
            base.Validate();

            var handle = ChannelHandle.TryParse(Alias);
            var slug = ChannelSlug.TryParse(Alias);
            var user = UserName.TryParse(Alias);
            var id = ChannelId.TryParse(Alias);
            validAliases = new object[] { handle, slug, user, id }.Where(id => id != null).ToArray();

            if (validAliases.Length == 0) throw new InputException(
                $"'{Alias}' is not a valid channel handle, slug, user name or channel ID.");
        }

        public async Task RemoteValidateAsync(YoutubeClient youtube, DataStore dataStore, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();

            /*  generate tasks checking which of the validIdentifiers are accessible
                (via knownChannelIdMaps cache or HTTP request) and execute them in parrallel */
            var (channels, maybeExceptions) = await ValueTasks.WhenAll(
                validAliases.Select(identifier => GetChannel(identifier)));

            #region rethrow unexpected exceptions
            var exceptions = maybeExceptions.Where(ex => ex is not null).ToArray();

            if (exceptions.Length > 0) throw new AggregateException(
                $"Unexpected errors identifying channel '{Alias}'.", exceptions);
            #endregion

            #region throw input exceptions if none or multiple channels are accessible
            var accessible = channels.Where(channel => channel != null).ToArray();
            if (accessible.Length == 0) throw new InputException($"Channel '{Alias}' could not be found.");

            var distinct = accessible.DistinctBy(channel => channel.Id);

            if (distinct.Count() > 1) throw new InputException($"Channel identifier '{Alias}' is ambiguous:"
                + Environment.NewLine + channels.Select((channel, index) =>
                {
                    if (channel == null) return null;
                    var validUrl = GetValidChannelUrl(validAliases.ElementAt(index));
                    var channelUrl = GetValidChannelUrl(channel.Id);
                    return $"{validUrl} points to channel {channelUrl}";
                })
                .Where(info => info != null).Join(Environment.NewLine)
                + Environment.NewLine + "Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.");
            #endregion

            var identified = distinct.Single();
            ID = identified.Id;
            ValidUrls = new[] { GetValidChannelUrl(identified.Id) };

            async ValueTask<Channel> GetChannel(object identifier)
            {
                var loadChannel = identifier is ChannelHandle handle ? youtube.Channels.GetByHandleAsync(handle, cancellation)
                    : identifier is ChannelSlug slug ? youtube.Channels.GetBySlugAsync(slug, cancellation)
                    : identifier is UserName user ? youtube.Channels.GetByUserAsync(user, cancellation)
                    : identifier is ChannelId id ? youtube.Channels.GetAsync(id, cancellation)
                    : throw new NotImplementedException($"Getting channel for alias {identifier.GetType()} is not implemented.");

                try { return await loadChannel; }
                catch (HttpRequestException ex)
                {
                    if (ex.IsNotFound()) return null;
                    else throw; // rethrow to raise assumed transient error
                }
            }
        }

        private static string GetValidChannelUrl(object id)
        {
            var urlGlue = id is ChannelHandle ? "@" : id is ChannelSlug ? "c/" : id is UserName ? "user/" : id is ChannelId ? "channel/"
                : throw new NotImplementedException($"Generating URL for channel identifier {id.GetType()} is not implemented.");

            return $"https://www.youtube.com/{urlGlue}{id}";
        }
        #endregion

        internal override IAsyncEnumerable<PlaylistVideo> GetVideosAsync(YoutubeClient youtube, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return youtube.Channels.GetUploadsAsync(ID, cancellation);
        }
    }
}