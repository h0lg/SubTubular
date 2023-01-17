using System.Collections.Generic;
using System.Threading;
using CommandLine;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;

namespace SubTubular
{
    [Verb("search-channel", aliases: new[] { "channel", "c" },
        HelpText = "Searches the videos in a channel's Uploads playlist."
        + $" This is a glorified '{SearchPlaylist.Command}'.")]
    internal sealed class SearchChannel : SearchPlaylistCommand
    {
        internal const string StorageKeyPrefix = "channel ";

        [Value(0, MetaName = "channel", Required = true, HelpText = "The channel ID or URL.")]
        public string Channel { get; set; }

        internal override string Label => StorageKeyPrefix;
        protected override string UrlFormat => "https://www.youtube.com/channel/";

        internal override void Validate()
        {
            base.Validate();

            var id = ChannelId.TryParse(Channel);
            if (id == null) throw new InputException($"'{Channel}' is not a valid channel ID.");
            ID = id;
        }

        internal override IAsyncEnumerable<PlaylistVideo> GetVideosAsync(YoutubeClient youtube, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return youtube.Channels.GetUploadsAsync(ID, cancellation);
        }
    }
}