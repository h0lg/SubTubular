using System.Diagnostics;
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
        List<Task> validations = new();

        if (command.Channels.HasAny()) validations.AddRange(
            command.Channels!.Select(channel => RemoteValidateAsync(channel, youtube, dataStore,
                cancellation, command.ProgressReporter?.CreateVideoListProgress(channel))));

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
        var video = await youtube.GetVideoAsync(validationResult.Id, cancellation, downloadCaptionTracksAndSave: false);
        validationResult.Video = video;
        progress?.Report(validationResult.Id, BatchProgress.Status.validated);
    }

    private static async Task RemoteValidateAsync(PlaylistScope scope, Youtube youtube, DataStore dataStore,
        CancellationToken cancellation, BatchProgressReporter.VideoListProgress? progress)
    {
        //progress?.Report(BatchProgress.Status.loading);
        scope.SingleValidated.Playlist = await youtube.GetPlaylistAsync(scope, cancellation, progress);
        progress?.Report(BatchProgress.Status.validated);
    }

    private static async Task RemoteValidateAsync(ChannelScope scope, Youtube youtube, DataStore dataStore,
        CancellationToken cancellation, BatchProgressReporter.VideoListProgress? progress)
    {
        cancellation.ThrowIfCancellationRequested();

        Debug.WriteLine("######### ChannelAliasMap.LoadList for " + scope.Identify());
        // load cached info about which channel aliases map to which channel IDs and which channel IDs are accessible
        var knownAliasMaps = await ChannelAliasMap.LoadList(dataStore) ?? [];
        Debug.WriteLine("######### ChannelAliasMap.LoadList done for " + scope.Identify());

        // remembers whether knownAliasMaps was changed across multiple calls of GetChannelAliasMap
        var knownAliasMapsUpdated = false;

        /*  generate tasks checking which of the validAliases are accessible
            (via knownAliasMaps cache or HTTP request) and execute them in parrallel */
        IEnumerable<object> wellStructuredAliases = scope.SingleValidated.WellStructuredAliases!;

        progress?.Report(BatchProgress.Status.downloading);
        var (aliasMaps, maybeExceptions) = await ValueTasks.WhenAll<ChannelAliasMap>(wellStructuredAliases.Select(GetChannelAliasMap));

        // cache accessibility of channel IDs and aliases locally to avoid subsequent HTTP requests
        if (knownAliasMapsUpdated)
        {
            Debug.WriteLine("######### ChannelAliasMap.SaveList for " + scope.Identify());
            await ChannelAliasMap.SaveList(knownAliasMaps, dataStore);
            Debug.WriteLine("######### ChannelAliasMap.SaveList done for " + scope.Identify());
        }

        #region rethrow unexpected exceptions
        var exceptions = maybeExceptions.Where(ex => ex is not null).ToArray();

        if (exceptions.Length > 0) throw new AggregateException(
            $"Unexpected errors identifying channel '{scope.Alias}'.", exceptions);
        #endregion

        #region throw input exceptions if Alias matches none or multiple accessible channels
        var accessibleMaps = aliasMaps.Where(map => map.ChannelId != null).ToArray();
        if (accessibleMaps.Length == 0) throw new InputException($"Channel '{scope.Alias}' could not be found.");

        var distinct = accessibleMaps.DistinctBy(map => map.ChannelId);

        if (distinct.Count() > 1) throw new InputException($"Channel alias '{scope.Alias}' is ambiguous:"
            + Environment.NewLine + accessibleMaps.Select(map =>
            {
                var validUrl = Youtube.GetChannelUrl(wellStructuredAliases.Single(id => id.GetType().Name == map.Type));
                var channelUrl = Youtube.GetChannelUrl((ChannelId)map.ChannelId!);
                return $"{validUrl} points to channel {channelUrl}";
            })
            .Join(Environment.NewLine)
            + Environment.NewLine + "Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.");
        #endregion

        string id = distinct.Single().ChannelId!;
        scope.SingleValidated.Id = id;
        scope.SingleValidated.Playlist = await youtube.GetPlaylistAsync(scope, cancellation, progress);
        scope.SingleValidated.Url = Youtube.GetChannelUrl((ChannelId)id);
        progress?.Report(BatchProgress.Status.validated);

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

            try
            {
                var channel = await loadChannel;
                map.ChannelId = channel.Id;
            }
            catch (HttpRequestException ex) when (ex.IsNotFound()) { map.ChannelId = null; }
            // otherwise rethrow to raise assumed transient error

            knownAliasMaps.Add(map);
            knownAliasMapsUpdated = true;
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