using System.CommandLine;
using SubTubular.Extensions;

namespace SubTubular.Shell;

using FloatListOption = Option<IEnumerable<float>?>;
using StringListOption = Option<IEnumerable<string>>;
using UshortListOption = Option<IEnumerable<ushort>?>;

static partial class CommandInterpreter
{
    private static (StringListOption channels, StringListOption playlists, StringListOption videos) AddScopes(Command outputCommand)
    {
        StringListOption channels = new(Scopes.channels)
        {
            Description = "The space-separated channel IDs, handles, slugs, user names and/or URLs for either of those.",
            AllowMultipleArgumentsPerToken = true
        };

        StringListOption playlists = new(Scopes.playlists)
        {
            Description = "The space-separated playlist IDs and/or URLs.",
            AllowMultipleArgumentsPerToken = true
        };

        StringListOption videos = new(Scopes.videos)
        {
            Description = "The space-separated YouTube video IDs and/or URLs." + quoteIdsStartingWithDash,
            AllowMultipleArgumentsPerToken = true
        };

        outputCommand.Options.Add(channels);
        outputCommand.Options.Add(playlists);
        outputCommand.Options.Add(videos);
        return (channels, playlists, videos);
    }

    internal const ushort SkipDefault = 0, TakeDefault = 50;
    internal const float CacheHoursDefault = 24f;

    private static (UshortListOption skip, UshortListOption take, FloatListOption cacheHours) AddPlaylistLikeCommandOptions(Command command)
    {
        UshortListOption skip = new(Args.skip, "-sk")
        {
            Description = "The number of videos to skip from the top in the searched channels (Uploads playlist) and playlists;"
                + " effectively limiting the search scope to the videos after it."
                + DescribePlaylistLikeOptionValues(SkipDefault),
            AllowMultipleArgumentsPerToken = true
        };

        UshortListOption take = new(Args.take, "-t")
        {
            Description = $"The number of videos to search from the top (or '{Args.skip}'-ed to part) of the searched channels (Uploads playlist) and playlists;"
                + " effectively limiting the search range."
                + DescribePlaylistLikeOptionValues(TakeDefault)
                + " You may want to gradually increase this to include all videos in the list while you're refining your query."
                + $" Note that the special Uploads playlist of a channel is sorted latest '{nameof(SearchCommand.OrderOptions.uploaded)}' first,"
                + " but custom playlists may be sorted differently. Keep that in mind if you don't find what you're looking for"
                + $" and when using '{Args.orderBy}' (which is only applied to the results) with '{nameof(SearchCommand.OrderOptions.uploaded)}' on custom playlists.",
            AllowMultipleArgumentsPerToken = true
        };

        FloatListOption cacheHours = new(Args.cacheHours, "-ch")
        {
            Description = "The maximum ages of the searched channel (Uploads playlist) and playlist caches in hours"
                + " before they're considered stale and the list of videos in them are refreshed."
                + DescribePlaylistLikeOptionValues(CacheHoursDefault)
                + " Note this doesn't apply to the videos themselves because their contents rarely change after upload."
                + $" Use '--{clearCacheCommand}' to clear videos associated with a playlist or channel if that's what you're after.",
            AllowMultipleArgumentsPerToken = true
        };

        command.Options.Add(skip);
        command.Options.Add(take);
        command.Options.Add(cacheHours);
        return (skip, take, cacheHours);
    }

    private static string DescribePlaylistLikeOptionValues(object defaultValue)
        => $" You can specify a value for each included scope, '{Scopes.channels}' before '{Scopes.playlists}', in the order they're passed."
            + " If you specify less values than scopes, the last value is used for remaining scopes."
            + $" If left empty, {defaultValue} is used for all scopes.";
}

internal static partial class BindingExtensions
{
    internal static T ValueAtIndexOrLastOrDefault<T>(this T[]? values, int index, T defaultValue)
        => values == null || values.Length == 0 ? defaultValue // use default if there are no items
            : index < values.Length ? values[index] // the value at the given index if it exists
            : values[^1]; // or the last value by default

    internal static T BindScopes<T>(this T command, ParseResult parsed,
        StringListOption videos, StringListOption channels, StringListOption playlists,
        UshortListOption skip, UshortListOption take, FloatListOption cacheHours) where T : OutputCommand
    {
        var videoIds = parsed.GetValue(videos);
        command.Videos = videoIds == null ? null : new VideosScope([.. videoIds]);

        var channelScopes = parsed.GetValue(channels);
        var playlistScopes = parsed.GetValue(playlists);
        int channelCount = channelScopes?.Count() ?? 0;
        var playlistLikes = channelCount + (playlistScopes?.Count() ?? 0);

        var skips = parsed.GetValue(skip)?.ToArray();
        var takes = parsed.GetValue(take)?.ToArray();
        var cacheHour = parsed.GetValue(cacheHours)?.ToArray();

        if (channelScopes.HasAny()) command.Channels = [.. channelScopes!.Select((alias, i) =>
        {
            (ushort skip, ushort take, float cacheHrs) = GetOptions(i);
            return new ChannelScope(alias, skip, take, cacheHrs);
        })];

        if (playlistScopes.HasAny()) command.Playlists = [.. playlistScopes!.Select((playlist, idx) =>
        {
            var i = channelCount + idx;
            (ushort skip, ushort take, float cacheHrs) = GetOptions(i);
            return new PlaylistScope(playlist, skip, take, cacheHrs);
        })];

        return command;

        (ushort skip, ushort take, float cacheHrs) GetOptions(int index)
            => (skips.ValueAtIndexOrLastOrDefault(index, CommandInterpreter.SkipDefault),
                takes.ValueAtIndexOrLastOrDefault(index, CommandInterpreter.TakeDefault),
                cacheHour.ValueAtIndexOrLastOrDefault(index, CommandInterpreter.CacheHoursDefault));
    }
}
