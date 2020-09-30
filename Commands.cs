using System.Collections.Generic;
using CommandLine;

namespace SubTubular
{
    internal abstract class SearchCommand
    {
        [Option('t', "Terms", Required = true, Separator = '/',
            HelpText = "What to search for. Seperate multiple/terms/or phrases/with slashes."
                + " When searching for phrases containig multiple words,"
                + " keep in mind that phrases containig multiple words may be broken up into different captions"
                + " and only single captions are matched. So keep your phrases short.")]
        public IEnumerable<string> Terms { get; set; }
    }

    [Verb("search-playlist", HelpText = "Downloads the transcripts/captions"
        + " of the {Latest} n videos of the playlist specified by {PlaylistId}"
        + " and searches them for the provided {Terms}.")]
    internal sealed class SearchPlaylist : SearchCommand
    {
        [Option('p', "PlaylistId", Required = true, HelpText = "The playlist ID.")]
        public string PlaylistId { get; set; }

        [Option('l', "Latest", Default = 50, HelpText = "The number of latest videos to download and search.")]
        public int Latest { get; set; }

        [Option('m', "CachePlaylistForMinutes", Default = 60 * 24, HelpText = "How many minutes to cache the playlist for.")]
        public int CachePlaylistForMinutes { get; set; }
    }

    [Verb("search-videos", HelpText = "Downloads the transcripts/captions"
        + " of the videos in {VideoIds} and searches them for the provided {Terms}.")]
    internal sealed class SearchVideos : SearchCommand
    {
        [Option('v', "VideoIds", Required = true, Separator = '/', HelpText = "The slash-separated YouTube video IDs.")]
        public IEnumerable<string> VideoIds { get; set; }
    }
}