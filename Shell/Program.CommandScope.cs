using System.CommandLine;
using System.CommandLine.Invocation;

namespace SubTubular.Shell;

static partial class Program
{
    static partial class CommandHandler
    {
        private static Argument<string> AddChannelAlias(Command channelCommand)
        {
            Argument<string> alias = new("channel", "The channel ID, handle, slug, user name or a URL for either of those.");
            channelCommand.AddArgument(alias);
            return alias;
        }

        private static Argument<string> AddPlaylistArgument(Command searchPlaylist)
        {
            Argument<string> playlist = new("playlist", "The playlist ID or URL.");
            searchPlaylist.AddArgument(playlist);
            return playlist;
        }

        private static Argument<IEnumerable<string>> AddVideosArgument(Command searchVideos)
        {
            Argument<IEnumerable<string>> videos = new("videos", "The space-separated YouTube video IDs and/or URLs." + quoteIdsStartingWithDash);
            searchVideos.AddArgument(videos);
            return videos;
        }

        private static ChannelScope CreateChannelScope(InvocationContext ctx, Argument<string> alias, Option<ushort> top, Option<IEnumerable<PlaylistLikeScope.OrderOptions>> orderBy, Option<float> cacheHours)
            => new ChannelScope { Alias = ctx.Parsed(alias), Top = ctx.Parsed(top), OrderBy = ctx.Parsed(orderBy), CacheHours = ctx.Parsed(cacheHours) };

        private static PlaylistScope CreatePlaylistScope(InvocationContext ctx, Argument<string> playlist,
            Option<ushort> top, Option<IEnumerable<PlaylistLikeScope.OrderOptions>> orderBy, Option<float> cacheHours)
            => new PlaylistScope
            {
                Playlist = ctx.Parsed(playlist),
                Top = ctx.Parsed(top),
                OrderBy = ctx.Parsed(orderBy),
                CacheHours = ctx.Parsed(cacheHours)
            };

        private static VideosScope CreateVideosScope(InvocationContext ctx, Argument<IEnumerable<string>> videos)
            => new VideosScope { Videos = ctx.Parsed(videos) };

        private static (Option<ushort> top, Option<IEnumerable<PlaylistLikeScope.OrderOptions>> orderBy, Option<float> cacheHours) AddPlaylistLikeCommandOptions(Command command)
        {
            const string topName = "--top", orderByName = "--order-by";

            Option<ushort> top = new(new[] { topName, "-t" }, () => 50,
                "The number of videos to search, counted from the top of the playlist;"
                + " effectively limiting the search scope to the top partition of it."
                + " You may want to gradually increase this to include all videos in the list while you're refining your query."
                + $" Note that the special Uploads playlist of a channel is sorted latest '{nameof(PlaylistLikeScope.OrderOptions.uploaded)}' first,"
                + " but custom playlists may be sorted differently. Keep that in mind if you don't find what you're looking for"
                + $" and when using '{orderByName}' (which is only applied to the results) with '{nameof(PlaylistLikeScope.OrderOptions.uploaded)}' on custom playlists.");

            Option<IEnumerable<PlaylistLikeScope.OrderOptions>> orderBy = new(new[] { orderByName, "-r" }, () => new[] { PlaylistLikeScope.OrderOptions.score },
                $"Order the video search results by '{nameof(PlaylistLikeScope.OrderOptions.uploaded)}'"
                + $" or '{nameof(PlaylistLikeScope.OrderOptions.score)}' with '{nameof(PlaylistLikeScope.OrderOptions.asc)}' for ascending."
                + $" The default is descending (i.e. latest respectively highest first) and by '{nameof(PlaylistLikeScope.OrderOptions.score)}'."
                + $" Note that the order is only applied to the results with the search scope itself being limited by the '{topName}' parameter."
                + " Note also that for un-cached videos, this option is ignored in favor of outputting matches as soon as they're found"
                + " - but simply repeating the search will hit the cache and return them in the requested order.")
            { AllowMultipleArgumentsPerToken = true };

            Option<float> cacheHours = new(new[] { "--cache-hours", "-ch" }, () => 24,
                "The maximum age of a playlist cache in hours"
                + " before it is considered stale and the list of videos in it is refreshed."
                + " Note this doesn't apply to the videos themselves because their contents rarely change after upload."
                + $" Use '--{clearCacheCommand}' to clear videos associated with a playlist or channel if that's what you're after.");

            command.AddOption(top);
            command.AddOption(orderBy);
            command.AddOption(cacheHours);
            return (top, orderBy, cacheHours);
        }
    }
}