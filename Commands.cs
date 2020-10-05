using System.Collections.Generic;
using CommandLine;
using YoutubeExplode;

namespace SubTubular
{
    internal abstract class SearchCommand
    {
        [Option('t', "Terms", Required = true, Separator = '/',
            HelpText = "What to search for. Seperate multiple/terms/or phrases/with slashes.")]
        public IEnumerable<string> Terms { get; set; }
    }

    internal abstract class SearchPlaylistCommand : SearchCommand
    {
        [Option('l', "Latest", Default = 50, HelpText = "The number of latest videos to download and search.")]
        public int Latest { get; set; }

        [Option('h', "CachePlaylistForHours", Default = 24, HelpText = "How many hours to cache the playlist for.")]
        public float CachePlaylistForHours { get; set; }

        internal abstract string GetStorageKey();
        internal abstract IAsyncEnumerable<YoutubeExplode.Videos.Video> GetVideosAsync(YoutubeClient youtube);
    }

    [Verb("search-channel", HelpText = "Downloads the transcripts/captions"
        + " of the {Latest} n videos of the Uploads playlist of the channel specified by {ChannelId}"
        + " and searches them for the provided {Terms}.")]
    internal sealed class SearchChannel : SearchPlaylistCommand
    {
        [Option('c', "ChannelId", Required = true, HelpText = "The channel ID.")]
        public string ChannelId { get; set; }

        internal override string GetStorageKey() => "channel " + ChannelId;

        internal override IAsyncEnumerable<YoutubeExplode.Videos.Video> GetVideosAsync(YoutubeClient youtube)
            => youtube.Channels.GetUploadsAsync(ChannelId);
    }

    [Verb("search-playlist", HelpText = "Downloads the transcripts/captions"
        + " of the {Latest} n videos of the playlist specified by {PlaylistId}"
        + " and searches them for the provided {Terms}.")]
    internal sealed class SearchPlaylist : SearchPlaylistCommand
    {
        [Option('p', "PlaylistId", Required = true, HelpText = "The playlist ID.")]
        public string PlaylistId { get; set; }

        internal override string GetStorageKey() => "playlist " + PlaylistId;

        internal override IAsyncEnumerable<YoutubeExplode.Videos.Video> GetVideosAsync(YoutubeClient youtube)
            => youtube.Playlists.GetVideosAsync(PlaylistId);
    }

    [Verb("search-videos", HelpText = "Downloads the transcripts/captions"
        + " of the videos in {VideoIds} and searches them for the provided {Terms}.")]
    internal sealed class SearchVideos : SearchCommand
    {
        [Option('v', "VideoIds", Required = true, Separator = '/', HelpText = "The slash-separated YouTube video IDs.")]
        public IEnumerable<string> VideoIds { get; set; }
    }

    [Verb("clear-cache", HelpText = "Clears cached channel, playlist and video info.")]
    internal sealed class ClearCache { }
}