using SubTubular.Extensions;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace SubTubular;

public static class CommandValidator
{
    // see Lifti.Querying.QueryTokenizer.ParseQueryTokens()
    private static readonly char[] controlChars = ['*', '%', '|', '&', '"', '~', '>', '?', '(', ')', '=', ','];

    public static void PrevalidateSearchCommand(SearchCommand command)
    {
        PrevalidateScopes(command);

        if (command.Query.IsNullOrWhiteSpace()) throw new InputException(
            $"The {nameof(SearchCommand.Query).ToLower()} is empty.");

        if (command.Query.HasAny() && command.Query!.All(c => controlChars.Contains(c))) throw new InputException(
            $"The {nameof(SearchCommand.Query).ToLower()} contains nothing but control characters."
            + " That'll stay unsupported unless you come up with a good reason for why it should be."
            + $" If you can, leave it at {AssemblyInfo.IssuesUrl} .");

        if (command.OrderBy.Intersect(SearchCommand.Orders).Count() > 1) throw new InputException(
            $"You may order by either '{nameof(SearchCommand.OrderOptions.score)}' or '{nameof(SearchCommand.OrderOptions.uploaded)}' (date), but not both.");
    }

    public static void PrevalidateScopes(OutputCommand command)
    {
        var errors = (new[]
        {
            TryGetScopeError(command.Playlists?.Select(Prevalidate), "{0} are not valid playlist IDs or URLs."),
            TryGetScopeError(command.Channels?.Select(Prevalidate), "{0} are not valid channel handles, slugs, user names or IDs."),
            TryGetScopeError(Prevalidate(command.Videos), "{0} are not valid video IDs or URLs.")
        }).WithValue().ToArray();

        if (errors.Length != 0) throw new InputException(errors.Join(Environment.NewLine));
        if (!command.HasPreValidatedScopes()) throw new InputException("No valid scope.");
    }

    internal static object[] PrevalidateChannelAlias(string alias)
    {
        var handle = ChannelHandle.TryParse(alias);
        var slug = ChannelSlug.TryParse(alias);
        var user = UserName.TryParse(alias);
        var id = ChannelId.TryParse(alias);
        return new object?[] { handle, slug, user, id }.WithValue().ToArray();
    }

    private static string? TryGetScopeError(IEnumerable<string?>? invalidIdentifiers, string format)
    {
        if (!invalidIdentifiers.HasAny()) return null;
        var withValue = invalidIdentifiers!.WithValue().ToArray();
        return withValue.Length == 0 ? null : string.Format(format, withValue.Join(", "));
    }

    private static string? Prevalidate(ChannelScope scope)
    {
        /*  validate Alias locally to throw eventual InputException,
            but store result for RemoteValidateChannelAsync */
        object[] wellStructuredAliases = PrevalidateChannelAlias(scope.Alias);
        if (!wellStructuredAliases.HasAny()) return scope.Alias; // return invalid

        scope.AddPrevalidated(scope.Alias, wellStructuredAliases);
        return null;
    }

    private static string? Prevalidate(PlaylistScope scope)
    {
        var id = PlaylistId.TryParse(scope.IdOrUrl);
        if (id == null) return scope.IdOrUrl; // return invalid

        scope.AddPrevalidated(id, "https://www.youtube.com/playlist?list=" + id);
        return null;
    }

    private static IEnumerable<string> Prevalidate(VideosScope? scope)
    {
        if (scope == null) return [];
        var idsToValid = scope.Videos.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"')));
        var validIds = idsToValid.Where(pair => pair.Value != null).Select(pair => pair.Value!.Value.ToString()).ToArray();
        foreach (var id in validIds) scope.AddPrevalidated(id, Youtube.GetVideoUrl(id));
        return scope.Videos.Except(validIds); // return invalid
    }

    public static async Task ValidateScopesAsync(OutputCommand command, Youtube youtube, DataStore dataStore, CancellationToken cancellation)
    {
        if (command.Channels.HasAny())
        {
            // load cached info about which channel aliases map to which channel IDs and which channel IDs are accessible
            var knownAliasMaps = await ChannelAliasMap.LoadList(dataStore) ?? [];

            var channelValidations = command.Channels!.Select(async channel =>
            {
                var discoveredMaps = await RemoteValidateAsync(channel, knownAliasMaps, youtube, dataStore,
                    cancellation, command.ProgressReporter?.CreateVideoListProgress(channel));

                string? error = null;

                if (discoveredMaps.Count() > 1) error = $"Channel alias '{channel.Alias}' is ambiguous:"
                    + Environment.NewLine + discoveredMaps.Select(map =>
                    {
                        var validUrl = Youtube.GetChannelUrl(channel.SingleValidated.WellStructuredAliases!.Single(id => id.GetType().Name == map.Type));
                        var channelUrl = Youtube.GetChannelUrl((ChannelId)map.ChannelId!);
                        return $"{validUrl} points to channel {channelUrl}";
                    })
                    .Join(Environment.NewLine)
                    + Environment.NewLine + "Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.";

                return (discoveredMaps, error);
            });

            var results = await Task.WhenAll(channelValidations);
            var discoveredMaps = results.SelectMany(pair => pair.discoveredMaps).Distinct();

            if (discoveredMaps.Any() && discoveredMaps.Select(knownAliasMaps.Add).Any(isNew => isNew))
                await ChannelAliasMap.SaveList(knownAliasMaps, dataStore);

            var errors = results.Select(pair => pair.error).WithValue();
            if (errors.Any()) throw new InputException(errors.Join(Environment.NewLine));
        }

        List<Task> validations = new();

        if (command.Playlists.HasAny()) validations.AddRange(
            command.Playlists!.Select(playlist => RemoteValidateAsync(playlist, youtube, dataStore,
                cancellation, command.ProgressReporter?.CreateVideoListProgress(playlist))));

        if (command.Videos?.IsPrevalidated == true) validations.AddRange(
            command.Videos!.Validated.Select(validationResult => RemoteValidateVideoAsync(validationResult, youtube, dataStore,
                cancellation, command.ProgressReporter?.CreateVideoListProgress(command.Videos!))));

        await Task.WhenAll(validations);
    }

    private static async Task RemoteValidateVideoAsync(CommandScope.ValidationResult validationResult, Youtube youtube, DataStore dataStore,
        CancellationToken cancellation, BatchProgressReporter.VideoListProgress? progress)
    {
        progress?.Report(validationResult.Id, BatchProgress.Status.downloading);
        //TODO not saved here
        validationResult.Video = await youtube.GetVideoAsync(validationResult.Id, cancellation, downloadCaptionTracksAndSave: false);
        progress?.Report(validationResult.Id, BatchProgress.Status.validated);
    }

    private static async Task RemoteValidateAsync(PlaylistScope scope, Youtube youtube, DataStore dataStore,
        CancellationToken cancellation, BatchProgressReporter.VideoListProgress? progress)
    {
        //progress?.Report(BatchProgress.Status.loading);
        scope.SingleValidated.Playlist = await youtube.GetPlaylistAsync(scope, cancellation, progress);
        progress?.Report(BatchProgress.Status.validated);
    }

    private static async Task<IEnumerable<ChannelAliasMap>> RemoteValidateAsync(ChannelScope channel, HashSet<ChannelAliasMap> knownAliasMaps, Youtube youtube, DataStore dataStore,
        CancellationToken cancellation, BatchProgressReporter.VideoListProgress? progress)
    {
        cancellation.ThrowIfCancellationRequested();

        /*  generate tasks checking which of the validAliases are accessible
            (via knownAliasMaps cache or HTTP request) and execute them in parrallel */
        var (matchingMaps, maybeExceptions) = await ValueTasks.WhenAll(channel.SingleValidated.WellStructuredAliases!.Select(GetChannelAliasMap));

        #region rethrow unexpected exceptions
        var exceptions = maybeExceptions.Where(ex => ex is not null).ToArray();

        if (exceptions.Length > 0) throw new AggregateException(
            $"Unexpected errors identifying channel '{channel.Alias}'.", exceptions);
        #endregion

        #region throw input exceptions if Alias matches none or multiple accessible channels
        var indentifiedMaps = matchingMaps.Where(map => map.ChannelId != null).ToArray();
        if (indentifiedMaps.Length == 0) throw new InputException($"Channel '{channel.Alias}' could not be found.");
        #endregion

        var distinctChannels = indentifiedMaps.DistinctBy(map => map.ChannelId).ToArray();
        if (distinctChannels.Length > 1) return distinctChannels;

        string id = distinctChannels.Single().ChannelId!;
        channel.SingleValidated.Id = id;
        channel.SingleValidated.Playlist = await youtube.GetPlaylistAsync(channel, cancellation, progress);
        channel.SingleValidated.Url = Youtube.GetChannelUrl((ChannelId)id);
        progress?.Report(BatchProgress.Status.validated);
        return distinctChannels;

        //refactor into get(channelid, Playlist)
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
            //progress?.Report(BatchProgress.Status.downloading);

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