using System.CommandLine;
using System.CommandLine.Invocation;

namespace SubTubular.Shell;

static partial class Program
{
    static partial class CommandHandler
    {
        private static (Option<IEnumerable<string>> channels, Option<IEnumerable<string>> playlists, Option<IEnumerable<string>> videos) AddScopes(Command outputCommand)
        {
            Option<IEnumerable<string>> channels = new("channels", "The channel IDs, handles, slugs, user names or URLs for either of those.") { AllowMultipleArgumentsPerToken = true };
            Option<IEnumerable<string>> playlists = new("playlists", "The playlist IDs or URLs.") { AllowMultipleArgumentsPerToken = true };
            Option<IEnumerable<string>> videos = new("videos", "The space-separated YouTube video IDs and/or URLs." + quoteIdsStartingWithDash) { AllowMultipleArgumentsPerToken = true };
            outputCommand.AddOption(channels);
            outputCommand.AddOption(playlists);
            outputCommand.AddOption(videos);
            return (channels, playlists, videos);
        }

        private static ChannelScope[]? CreateChannelScopes(InvocationContext ctx, Option<IEnumerable<string>> aliases,
            Option<ushort> top, Option<float> cacheHours)
            => ctx.Parsed(aliases)?.Select(alias => new ChannelScope(alias, ctx.Parsed(top), ctx.Parsed(cacheHours))).ToArray();

        private static PlaylistScope[]? CreatePlaylistScopes(InvocationContext ctx, Option<IEnumerable<string>> playlists,
            Option<ushort> top, Option<float> cacheHours)
            => ctx.Parsed(playlists)?.Select(playlist => new PlaylistScope(playlist, ctx.Parsed(top), ctx.Parsed(cacheHours))).ToArray();

        private static VideosScope? CreateVideosScope(InvocationContext ctx, Option<IEnumerable<string>> videos)
        {
            var ids = ctx.Parsed(videos);
            return ids == null ? null : new(ids);
        }

        private static (Option<ushort> top, Option<float> cacheHours) AddPlaylistLikeCommandOptions(Command command)
        {
            Option<ushort> top = new([topName, "-t"], () => 50,
                "The number of videos to search, counted from the top of the playlist;"
                + " effectively limiting the search scope to the top partition of it."
                + " You may want to gradually increase this to include all videos in the list while you're refining your query."
                + $" Note that the special Uploads playlist of a channel is sorted latest '{nameof(SearchCommand.OrderOptions.uploaded)}' first,"
                + " but custom playlists may be sorted differently. Keep that in mind if you don't find what you're looking for"
                + $" and when using '{orderByName}' (which is only applied to the results) with '{nameof(SearchCommand.OrderOptions.uploaded)}' on custom playlists.");

            Option<float> cacheHours = new(["--cache-hours", "-ch"], () => 24,
                "The maximum age of a playlist cache in hours"
                + " before it is considered stale and the list of videos in it is refreshed."
                + " Note this doesn't apply to the videos themselves because their contents rarely change after upload."
                + $" Use '--{clearCacheCommand}' to clear videos associated with a playlist or channel if that's what you're after.");

            command.AddOption(top);
            command.AddOption(cacheHours);
            return (top, cacheHours);
        }
    }
}