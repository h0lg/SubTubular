using System.CommandLine;
using System.CommandLine.Invocation;
using SubTubular.Extensions;

namespace SubTubular.Shell;

static partial class CommandInterpreter
{
    private static (Option<IEnumerable<string>> channels, Option<IEnumerable<string>> playlists, Option<IEnumerable<string>> videos) AddScopes(Command outputCommand)
    {
        Option<IEnumerable<string>> channels = new(Scopes.channels, "The channel IDs, handles, slugs, user names or URLs for either of those.") { AllowMultipleArgumentsPerToken = true };
        Option<IEnumerable<string>> playlists = new(Scopes.playlists, "The playlist IDs or URLs.") { AllowMultipleArgumentsPerToken = true };
        Option<IEnumerable<string>> videos = new(Scopes.videos, "The space-separated YouTube video IDs and/or URLs." + quoteIdsStartingWithDash) { AllowMultipleArgumentsPerToken = true };
        outputCommand.AddOption(channels);
        outputCommand.AddOption(playlists);
        outputCommand.AddOption(videos);
        return (channels, playlists, videos);
    }

    private static (Option<IEnumerable<ushort>> skip, Option<IEnumerable<ushort>> take, Option<IEnumerable<float>> cacheHours) AddPlaylistLikeCommandOptions(Command command)
    {
        Option<IEnumerable<ushort>> skip = new([Args.skip, "-sk"], () => [],
            "The number of videos to skip from the top in the searched channels (Uploads playlist) and playlists;"
            + " effectively limiting the search scope to the videos after it."
            + " Specify values for searched channels before those for searched playlists, each group in the order they're passed."
            + " If left empty, 0 is used for all channels and playlists.")
        { AllowMultipleArgumentsPerToken = true };

        Option<IEnumerable<ushort>> take = new([Args.take, "-t"], () => [],
            $"The number of videos to search from the top (or '{Args.skip}'-ed to part) of the searched channels (Uploads playlist) and playlists;"
            + " effectively limiting the search range."
            + " Specify values for searched channels before those for searched playlists, each group in the order they're passed."
            + " If left empty, 50 is used for all channels and playlists."
            + " You may want to gradually increase this to include all videos in the list while you're refining your query."
            + $" Note that the special Uploads playlist of a channel is sorted latest '{nameof(SearchCommand.OrderOptions.uploaded)}' first,"
            + " but custom playlists may be sorted differently. Keep that in mind if you don't find what you're looking for"
            + $" and when using '{Args.orderBy}' (which is only applied to the results) with '{nameof(SearchCommand.OrderOptions.uploaded)}' on custom playlists.")
        { AllowMultipleArgumentsPerToken = true };

        Option<IEnumerable<float>> cacheHours = new([Args.cacheHours, "-ch"], () => [],
            "The maximum ages of the searched channel (Uploads playlist) and playlist caches in hours"
            + " before they're considered stale and the list of videos in them are refreshed."
            + " Specify values for searched channels before those for searched playlists, each group in the order they're passed."
            + " If left empty, 24 is used for all channels and playlists."
            + " Note this doesn't apply to the videos themselves because their contents rarely change after upload."
            + $" Use '--{clearCacheCommand}' to clear videos associated with a playlist or channel if that's what you're after.")
        { AllowMultipleArgumentsPerToken = true };

        command.AddOption(skip);
        command.AddOption(take);
        command.AddOption(cacheHours);
        return (skip, take, cacheHours);
    }
}

internal static partial class BindingExtensions
{
    internal static T BindScopes<T>(this T command, InvocationContext ctx,
        Option<IEnumerable<string>> videos, Option<IEnumerable<string>> channels, Option<IEnumerable<string>> playlists,
        Option<IEnumerable<ushort>> skip, Option<IEnumerable<ushort>> take, Option<IEnumerable<float>> cacheHours) where T : OutputCommand
    {
        var videoIds = ctx.Parsed(videos);
        command.Videos = videoIds == null ? null : new VideosScope(videoIds.ToList());

        var channelScopes = ctx.Parsed(channels);
        var playlistScopes = ctx.Parsed(playlists);
        int channelCount = channelScopes?.Count() ?? 0;
        var playlistLikes = channelCount + (playlistScopes?.Count() ?? 0);

        var skips = ctx.Parsed(skip)?.ToArray() ?? Enumerable.Repeat((ushort)0, playlistLikes).ToArray();
        var takes = ctx.Parsed(take)?.ToArray() ?? Enumerable.Repeat((ushort)50, playlistLikes).ToArray();
        var cacheHour = ctx.Parsed(cacheHours)?.ToArray() ?? Enumerable.Repeat(24f, playlistLikes).ToArray();

        var errors = new List<string>();
        if (skips.Length != playlistLikes) errors.Add($"The number of values for '{Args.skip}' must match the sum of searched channels and playlists.");
        if (takes.Length != playlistLikes) errors.Add($"The number of values for '{Args.take}' must match the sum of searched channels and playlists.");
        if (cacheHour.Length != playlistLikes) errors.Add($"The number of values for '{Args.cacheHours}' must match the sum of searched channels and playlists.");
        if (errors.Count > 0) ctx.ParseResult.CommandResult.ErrorMessage = errors.Join(Environment.NewLine);

        if (channelScopes.HasAny()) command.Channels = channelScopes!.Select((alias, i)
            => new ChannelScope(alias, skips[i], takes[i], cacheHour[i])).ToArray();

        if (playlistScopes.HasAny()) command.Playlists = playlistScopes!.Select((playlist, idx) =>
        {
            var i = channelCount + idx;
            return new PlaylistScope(playlist, skips[i], takes[i], cacheHour[i]);
        }).ToArray();

        return command;
    }
}
