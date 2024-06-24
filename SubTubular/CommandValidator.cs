using SubTubular.Extensions;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace SubTubular;

/// <summary>Pre-validates <see cref="CommandScope"/>s and <see cref="OutputCommand"/>s,
/// i.e. checks locally for valid syntax.</summary>
public static class Prevalidate
{
    // see Lifti.Querying.QueryTokenizer.ParseQueryTokens()
    private static readonly char[] controlChars = ['*', '%', '|', '&', '"', '~', '>', '?', '(', ')', '=', ','];

    public static void Search(SearchCommand command)
    {
        Scopes(command);

        if (command.Query.IsNullOrWhiteSpace()) throw new InputException(
            $"The {nameof(SearchCommand.Query).ToLower()} is empty.");

        if (command.Query.HasAny() && command.Query!.All(c => controlChars.Contains(c))) throw new InputException(
            $"The {nameof(SearchCommand.Query).ToLower()} contains nothing but control characters."
            + " That'll stay unsupported unless you come up with a good reason for why it should be."
            + $" If you can, leave it at {AssemblyInfo.IssuesUrl} .");

        if (command.OrderBy.Intersect(SearchCommand.Orders).Count() > 1) throw new InputException(
            $"You may order by either '{nameof(SearchCommand.OrderOptions.score)}' or '{nameof(SearchCommand.OrderOptions.uploaded)}' (date), but not both.");
    }

    public static void Scopes(OutputCommand command)
    {
        var errors = (new[]
        {
            TryGetScopeError(command.Playlists?.Select(Playlist), "{0} are not valid playlist IDs or URLs."),
            TryGetScopeError(command.Channels?.Select(Channel), "{0} are not valid channel handles, slugs, user names or IDs."),
            TryGetScopeError(Videos(command.Videos), "{0} are not valid video IDs or URLs.")
        }).WithValue().ToArray();

        if (errors.Length != 0) throw new InputException(errors.Join(Environment.NewLine));
        if (!command.HasPreValidatedScopes()) throw new InputException("No valid scope.");
    }

    private static string? TryGetScopeError(IEnumerable<string?>? invalidIdentifiers, string format)
    {
        if (!invalidIdentifiers.HasAny()) return null;
        var withValue = invalidIdentifiers!.WithValue().ToArray();
        return withValue.Length == 0 ? null : string.Format(format, withValue.Join(", "));
    }

    internal static object[] ChannelAlias(string alias)
    {
        var handle = ChannelHandle.TryParse(alias);
        var slug = ChannelSlug.TryParse(alias);
        var user = UserName.TryParse(alias);
        var id = ChannelId.TryParse(alias);
        return new object?[] { handle, slug, user, id }.WithValue().ToArray();
    }

    private static string? Channel(ChannelScope scope)
    {
        /*  validate Alias locally to throw eventual InputException,
            but store result for RemoteValidateChannelAsync */
        object[] wellStructuredAliases = ChannelAlias(scope.Alias);
        if (!wellStructuredAliases.HasAny()) return scope.Alias; // return invalid

        scope.Validated.Add(new CommandScope.ValidationResult { Id = scope.Alias, WellStructuredAliases = wellStructuredAliases });
        return null;
    }

    private static string? Playlist(PlaylistScope scope)
    {
        var id = PlaylistId.TryParse(scope.Alias);
        if (id == null) return scope.Alias; // return invalid

        scope.AddPrevalidated(id, "https://www.youtube.com/playlist?list=" + id);
        return null;
    }

    private static IEnumerable<string> Videos(VideosScope? scope)
    {
        if (scope == null) return [];
        var idsToValid = scope.Videos.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"'))?.ToString());
        var validIds = idsToValid.Where(pair => pair.Value != null).Select(pair => pair.Value!).Distinct().ToArray();
        foreach (var id in validIds) scope.AddPrevalidated(id, Youtube.GetVideoUrl(id));
        return scope.Videos.Except(validIds); // return invalid
    }
}

public static class CommandValidator
{
    public static async Task ValidateScopesAsync(OutputCommand command, Youtube youtube, DataStore dataStore, CancellationToken cancellation)
    {
        List<Task> validations = [];

        if (command.Channels.HasAny())
        {
            // load cached info about which channel aliases map to which channel IDs and which channel IDs are accessible
            var knownAliasMaps = await ChannelAliasMap.LoadList(dataStore) ?? [];

            var channelValidations = command.Channels!.Select(async channel =>
            {
                var matchingChannels = await RemoteValidateAsync(channel, knownAliasMaps, youtube,
                    command.ProgressReporter?.CreateVideoListProgress(channel), cancellation);

                string? error = null;

                if (matchingChannels.Length > 1) error =
                    matchingChannels.Select(map =>
                    {
                        var validUrl = Youtube.GetChannelUrl(channel.SingleValidated.WellStructuredAliases!.Single(id => id.GetType().Name == map.Type));
                        var channelUrl = Youtube.GetChannelUrl((ChannelId)map.ChannelId!);
                        return $"{validUrl} points to channel {channelUrl}";
                    })
                    .Prepend($"Channel alias '{channel.Alias}' is ambiguous:")
                    .Append("Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.")
                    .Join(Environment.NewLine);

                return (matchingChannels, error);
            });

            var channelsValidated = Task.WhenAll(channelValidations).ContinueWith(async task =>
            {
                var matchingChannels = task.Result.SelectMany(pair => pair.matchingChannels).Distinct();

                // save the knownAliasMaps HashSet if adding any matching channel returns true indicating that it was new
                if (matchingChannels.Any() && matchingChannels.Select(knownAliasMaps.Add).Any(isNew => isNew))
                    await ChannelAliasMap.SaveList(knownAliasMaps, dataStore);

                var errors = task.Result.Select(pair => pair.error).WithValue();
                if (errors.Any()) throw new InputException(errors.Join(Environment.NewLine));
            }).WithAggregateException();

            validations.Add(channelsValidated);
        }

        if (command.Playlists.HasAny()) validations.AddRange(
            command.Playlists!.Select(playlist => RemoteValidateAsync(playlist, youtube,
                command.ProgressReporter?.CreateVideoListProgress(playlist), cancellation)));

        if (command.Videos?.IsPrevalidated == true) validations.AddRange(
            command.Videos!.Validated.Select(validationResult => RemoteValidateVideoAsync(validationResult, youtube, dataStore,
                command.ProgressReporter?.CreateVideoListProgress(command.Videos!), cancellation)));

        await Task.WhenAll(validations).WithAggregateException();
    }

    private static async Task RemoteValidateVideoAsync(CommandScope.ValidationResult validationResult, Youtube youtube, DataStore dataStore,
        BatchProgressReporter.VideoListProgress? progress, CancellationToken cancellation)
    {
        progress?.Report(validationResult.Id, BatchProgress.Status.downloading);
        // video is not saved here without captiontracks so none in the cache means there probably are none - otherwise cached info is indeterminate
        validationResult.Video = await youtube.GetVideoAsync(validationResult.Id, cancellation, downloadCaptionTracksAndSave: false);
        progress?.Report(validationResult.Id, BatchProgress.Status.validated);
    }

    private static async Task RemoteValidateAsync(PlaylistScope scope, Youtube youtube,
        BatchProgressReporter.VideoListProgress? progress, CancellationToken cancellation)
    {
        progress?.Report(BatchProgress.Status.loading);
        scope.SingleValidated.Playlist = await youtube.GetPlaylistAsync(scope, cancellation, progress);
        progress?.Report(BatchProgress.Status.validated);
    }

    private static async Task<ChannelAliasMap[]> RemoteValidateAsync(ChannelScope channel, HashSet<ChannelAliasMap> knownAliasMaps, Youtube youtube,
        BatchProgressReporter.VideoListProgress? progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        /*  generate tasks checking which of the validAliases are accessible
            (via knownAliasMaps cache or HTTP request) and execute them in parrallel */
        var (matchingChannels, maybeExceptions) = await ValueTasks.WhenAll(channel.SingleValidated.WellStructuredAliases!.Select(GetChannelAliasMap));

        #region rethrow unexpected exceptions
        var exceptions = maybeExceptions.Where(ex => ex is not null).ToArray();

        if (exceptions.Length > 0) throw new AggregateException(
            $"Unexpected errors identifying channel '{channel.Alias}'.", exceptions);
        #endregion

        #region throw input exceptions if Alias matches none or multiple accessible channels
        var indentifiedChannels = matchingChannels.Where(map => map.ChannelId != null).ToArray();
        if (indentifiedChannels.Length == 0) throw new InputException($"Channel '{channel.Alias}' could not be found.");
        #endregion

        var distinctChannels = indentifiedChannels.DistinctBy(map => map.ChannelId).ToArray();
        if (distinctChannels.Length > 1) return distinctChannels;

        string id = distinctChannels.Single().ChannelId!;
        channel.SingleValidated.Id = id;
        channel.SingleValidated.Playlist = await youtube.GetPlaylistAsync(channel, cancellation, progress);
        channel.SingleValidated.Url = Youtube.GetChannelUrl((ChannelId)id);
        progress?.Report(BatchProgress.Status.validated);
        return distinctChannels;

        async ValueTask<ChannelAliasMap> GetChannelAliasMap(object alias)
        {
            var map = knownAliasMaps.ForAlias(alias);
            if (map != null) return map; // use cached info

            var loadChannel = alias is ChannelHandle handle ? youtube.Client.Channels.GetByHandleAsync(handle, cancellation)
                : alias is ChannelSlug slug ? youtube.Client.Channels.GetBySlugAsync(slug, cancellation)
                : alias is UserName user ? youtube.Client.Channels.GetByUserAsync(user, cancellation)
                : alias is ChannelId id ? youtube.Client.Channels.GetAsync(id, cancellation)
                : throw new NotImplementedException($"Getting channel for alias {alias.GetType()} is not implemented.");

            var (type, value) = ChannelAliasMap.GetTypeAndValue(alias);
            map = new ChannelAliasMap { Type = type, Value = value };
            progress?.Report(BatchProgress.Status.downloading);

            try
            {
                var channel = await loadChannel;
                map.ChannelId = channel.Id;
            }
            catch (HttpRequestException ex) when (ex.IsNotFound()) { map.ChannelId = null; }
            // otherwise rethrow to raise assumed transient error

            return map;
        }
    }
}

[Serializable]
public class InputException : Exception
{
    public InputException(string message) : base(message) { }
    public InputException(string message, Exception innerException) : base(message, innerException) { }
}