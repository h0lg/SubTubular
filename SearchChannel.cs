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
        + " This is a glorified search-playlist.")]
    internal sealed class SearchChannel : SearchPlaylistCommand
    {
        internal const string StorageKeyPrefix = "channel ";

        [Value(0, MetaName = "channel", Required = true, HelpText = "The channel ID or URL.")]
        public string Channel { get; set; }

        internal override string Label => StorageKeyPrefix;
        protected override string ID => ChannelId.Parse(Channel).Value;
        protected override string UrlFormat => "https://www.youtube.com/channel/";

        internal override IAsyncEnumerable<PlaylistVideo> GetVideosAsync(YoutubeClient youtube, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return youtube.Channels.GetUploadsAsync(Channel, cancellation);
        }
    }
}