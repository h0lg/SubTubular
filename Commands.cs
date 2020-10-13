using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CommandLine;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace SubTubular
{
    internal abstract class SearchCommand
    {
        private string[] terms;

        [Option('f', "for", Required = true, Separator = ',', HelpText = "What to search for."
            + " Quote \"multi-word phrases\" and \"separate,multiple terms,by comma\".")]
        public IEnumerable<string> Terms
        {
            get => terms;
            set => terms = value
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().Replace(@"\s+", " ")) //trim and replace multiple whitespaces with just one space
                .Distinct()
                .ToArray();
        }

        [Option('m', "html",
            HelpText = "If set, outputs the highlighted search result in an HTML file including hyperlinks for easy navigation."
                + " The output path depends on the 'out' parameter.")]
        public bool OutputHtml { get; set; }

        [Option('o', "out",
            HelpText = "Writes the search results to a file, the format of which - depending on the 'html' flag -"
                + " is either text or HTML including hyperlinks for easy navigation."
                + " Supply EITHER the FULL FILE PATH (any existing file will be overwritten),"
                + " a FOLDER PATH to output files into - auto-named according to your search parameters -"
                + " OR OMIT while setting the 'html' flag to have auto-named files written"
                + " to the 'out' folder of SubTubular's AppData directory.")]
        public string FileOutputPath { get; set; }

        protected abstract string FormatInternal();
        internal string Format() => FormatInternal() + " " + Terms.Join(" ");
    }

    internal abstract class SearchPlaylistCommand : SearchCommand
    {
        [Option('t', "top", Default = 50,
            HelpText = "The number of videos to return from the top of the playlist."
                + " The special Uploads playlist of a channel or user are sorted latest uploaded first,"
                + " but custom playlists may be sorted differently.")]
        public int Top { get; set; }

        [Option('h', "cachehours", Default = 24, HelpText = "The maximum age of a playlist cache in hours"
            + " before it is considered stale and the videos in it are refreshed.")]
        public float CacheHours { get; set; }

        internal abstract string GetStorageKey();
        protected override string FormatInternal() => GetStorageKey();
        internal abstract IAsyncEnumerable<PlaylistVideo> GetVideosAsync(YoutubeClient youtube, CancellationToken cancellation);
    }

    [Verb("search-user",
        HelpText = "Searches the {top} n videos from the Uploads playlist of the {user}'s channel for the specified {terms}."
            + " This is a glorified search-playlist.")]
    internal sealed class SearchUser : SearchPlaylistCommand
    {
        [Value(0, Required = true, HelpText = "The user name or URL.")]
        public string User { get; set; }

        internal override string GetStorageKey() => "user " + UserName.Parse(User).Value;

        internal override async IAsyncEnumerable<PlaylistVideo> GetVideosAsync(YoutubeClient youtube, [EnumeratorCancellation] CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var channel = await youtube.Channels.GetByUserAsync(User, cancellation);

            await foreach (var video in youtube.Channels.GetUploadsAsync(channel.Id, cancellation))
            {
                cancellation.ThrowIfCancellationRequested();
                yield return video;
            }
        }
    }

    [Verb("search-channel",
        HelpText = "Searches the {top} n videos from the Uploads playlist of the {channel} for the specified {terms}."
        + " This is a glorified search-playlist.")]
    internal sealed class SearchChannel : SearchPlaylistCommand
    {
        [Value(0, Required = true, HelpText = "The channel ID or URL.")]
        public string Channel { get; set; }

        internal override string GetStorageKey() => "channel " + ChannelId.Parse(Channel).Value;

        internal override IAsyncEnumerable<PlaylistVideo> GetVideosAsync(YoutubeClient youtube, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return youtube.Channels.GetUploadsAsync(Channel, cancellation);
        }
    }

    [Verb("search-playlist", HelpText = "Searches the {top} n videos from the {playlist} for the specified {terms}.")]
    internal sealed class SearchPlaylist : SearchPlaylistCommand
    {
        [Value(0, Required = true, HelpText = "The playlist ID or URL.")]
        public string Playlist { get; set; }

        internal override string GetStorageKey() => "playlist " + PlaylistId.Parse(Playlist).Value;

        internal override IAsyncEnumerable<PlaylistVideo> GetVideosAsync(YoutubeClient youtube, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return youtube.Playlists.GetVideosAsync(Playlist, cancellation);
        }
    }

    [Verb("search-videos", HelpText = "Searches the {videos} for the specified {terms}.")]
    internal sealed class SearchVideos : SearchCommand
    {
        [Value(0, Required = true, HelpText = "The space-separated YouTube video IDs and/or URLs.")]
        public IEnumerable<string> Videos { get; set; }

        internal IEnumerable<string> GetVideoIds() => Videos.Select(v => VideoId.Parse(v).Value);
        protected override string FormatInternal() => "videos " + GetVideoIds().Join(" ");
    }

    [Verb("clear-cache", HelpText = "Clears cached user, channel, playlist and video info.")]
    internal sealed class ClearCache { }
}