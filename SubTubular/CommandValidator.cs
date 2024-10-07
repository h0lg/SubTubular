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
            Videos(command.Videos)
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

    /// <summary>Prevalidates the <paramref name="scope"/> returning validation errors.</summary>
    public static string? Scope(CommandScope scope)
    {
        if (scope is ChannelScope channel)
        {
            string? invalid = Channel(channel);
            if (invalid != null) return $"{invalid} is not a valid channel handle, slug, user name or ID.";
        }
        else if (scope is PlaylistScope playlist)
        {
            string? invalid = Playlist(playlist);
            if (invalid != null) return $"{invalid} is not a valid playlist ID or URL.";
        }
        else if (scope is VideosScope videos) return Videos(videos);

        return null;
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
        if (scope.IsPrevalidated) return null;

        /*  validate Alias locally to throw eventual InputException,
            but store result for RemoteValidateChannelAsync */
        object[] wellStructuredAliases = ChannelAlias(scope.Alias);
        if (!wellStructuredAliases.HasAny()) return scope.Alias; // return invalid

        scope.Validated.Add(new CommandScope.ValidationResult { Id = scope.Alias, WellStructuredAliases = wellStructuredAliases });
        scope.Report(VideoList.Status.preValidated);
        return null;
    }

    private static string? Playlist(PlaylistScope scope)
    {
        var id = PlaylistId.TryParse(scope.Alias);
        if (id == null) return scope.Alias; // return invalid

        if (scope.IsPrevalidated)
        {
            if (scope.SingleValidated.Id == id) return null; // already pre-validated
            scope.Validated.Clear(); // SingleValidated is out of date, remove it
        }

        scope.AddPrevalidated(id, Youtube.GetPlaylistUrl(id));
        scope.Report(VideoList.Status.preValidated);
        return null;
    }

    private static string? Videos(VideosScope? videos)
        => TryGetScopeError(VideosSeperately(videos), "{0} are not valid video IDs or URLs.");

    private static string[] VideosSeperately(VideosScope? scope)
    {
        if (scope == null) return [];
        var idsToValid = scope.Videos.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"'))?.ToString());
        var validIds = idsToValid.Select(pair => pair.Value).WithValue().Distinct().ToArray();
        scope.QueueVideos(validIds); // ignores already queued
        var alreadyValidated = scope.GetValidatedIds();

        foreach (var id in validIds.Except(alreadyValidated))
        {
            scope.AddPrevalidated(id, Youtube.GetVideoUrl(id));
            scope.Report(id, VideoList.Status.preValidated);
        }

        var invalidIds = idsToValid.Where(pair => !validIds.Contains(pair.Value)).Select(pair => pair.Key).ToArray();
        if (invalidIds.Length == 0) scope.Report(VideoList.Status.preValidated);
        return invalidIds; // return invalid
    }
}

/// <summary>Remotely validates <see cref="CommandScope"/>s and <see cref="OutputCommand"/>s,
/// i.e. checking for their existence and uniqueness.</summary>
public static class RemoteValidate
{
    public static async Task ScopesAsync(OutputCommand command, Youtube youtube, DataStore dataStore, CancellationToken cancellation)
    {
        List<Task> validations = [];

        if (command.Channels.HasAny())
            validations.Add(ChannelsAsync(command.Channels!, youtube, dataStore, cancellation));

        if (command.Playlists.HasAny()) validations.AddRange(
            command.Playlists!.Select(playlist => PlaylistAsync(playlist, youtube, cancellation)));

        if (command.Videos?.IsPrevalidated == true)
            validations.AddRange(Videos(command.Videos!, youtube, cancellation));

        await Task.WhenAll(validations).WithAggregateException();
        if (command.Videos?.IsValid == true) command.Videos.Report(VideoList.Status.validated);
    }

    public static async Task AllVideosAsync(VideosScope videosScope, Youtube youtube, CancellationToken cancellation)
    {
        await Task.WhenAll(Videos(videosScope, youtube, cancellation)).WithAggregateException();
        if (videosScope.IsValid) videosScope.Report(VideoList.Status.validated);
    }

    private static IEnumerable<Task> Videos(VideosScope videosScope, Youtube youtube, CancellationToken cancellation)
        // rely on pre-validation to have added an entry in Validated, skipping previously remote-validated
        => videosScope.GetRemoteValidated(false).Select(async validationResult =>
        {
            // video is not saved here without captiontracks so none in the cache means there probably are none - otherwise cached info is indeterminate
            validationResult.Video = await youtube.GetVideoAsync(validationResult.Id, cancellation, videosScope, downloadCaptionTracksAndSave: false);
            videosScope.Report(validationResult.Id, VideoList.Status.validated);
        });

    public static async Task PlaylistAsync(PlaylistScope scope, Youtube youtube, CancellationToken cancellation)
    {
        scope.SingleValidated.Playlist = await youtube.GetPlaylistAsync(scope, cancellation);
        scope.Report(VideoList.Status.validated);
    }

    public static async Task ChannelsAsync(ChannelScope[] channelScopes, Youtube youtube, DataStore dataStore, CancellationToken cancellation)
    {
        // load cached info about which channel aliases map to which channel IDs and which channel IDs are accessible
        var knownAliasMaps = await ChannelAliasMap.LoadListAsync(dataStore);

        var channelValidations = channelScopes.Select(async channel =>
        {
            string? error = null;
            ChannelAliasMap[] matchingChannels = [];

            try { matchingChannels = await ChannelAsync(channel, knownAliasMaps, youtube, cancellation); }
            catch (InputException ex) { error = ex.Message; } // record input exceptions separately

            if (matchingChannels.Length > 1)
            {
                var lines = matchingChannels.Select(map =>
                {
                    var validUrl = Youtube.GetChannelUrl(channel.SingleValidated.WellStructuredAliases!.Single(id => id.GetType().Name == map.Type));
                    var channelUrl = Youtube.GetChannelUrl((ChannelId)map.ChannelId!);
                    return $"{validUrl} points to channel {channelUrl}";
                })
                .Prepend($"Channel alias '{channel.Alias}' is ambiguous:")
                .Append("Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.");

                if (error != null) lines = lines.Prepend(error);
                error = lines.Join(Environment.NewLine);
            }

            return (matchingChannels, error);
        }).ToArray();

        List<Exception> errors = [];

        try
        {
            await Task.WhenAll(channelValidations).WithAggregateException();
        }
        catch (Exception ex)
        {
            if (ex is AggregateException agg) errors.AddRange(agg.InnerExceptions);
            else errors.Add(ex);
        }

        var results = channelValidations.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray();
        var matchingChannels = results.SelectMany(r => r.matchingChannels).Distinct();

        // save the knownAliasMaps HashSet if adding any matching channel returns true indicating that it was new
        if (matchingChannels.Any()) await ChannelAliasMap.AddEntriesAsync(matchingChannels, dataStore);

        // merge input errors
        errors.AddRange(results.Select(r => r.error).WithValue().Select(error => new InputException(error)));
        if (errors.Count == 1) throw errors.Single();
        if (errors.Count > 1) throw new AggregateException(errors);
    }

    private static async Task<ChannelAliasMap[]> ChannelAsync(ChannelScope channel,
        HashSet<ChannelAliasMap> knownAliasMaps, Youtube youtube, CancellationToken cancellation)
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

        #region throw input exception if Alias matches no accessible channel
        var indentifiedChannels = matchingChannels.Where(map => map.ChannelId != null).ToArray();
        if (indentifiedChannels.Length == 0) throw new InputException($"Channel '{channel.Alias}' could not be found.");
        #endregion

        var distinctChannels = indentifiedChannels.DistinctBy(map => map.ChannelId).ToArray();
        if (distinctChannels.Length > 1) return distinctChannels;

        string id = distinctChannels.Single().ChannelId!;
        channel.SingleValidated.Id = id;
        channel.SingleValidated.Playlist = await youtube.GetPlaylistAsync(channel, cancellation);
        channel.SingleValidated.Url = Youtube.GetChannelUrl((ChannelId)id);
        channel.Report(VideoList.Status.validated);
        return distinctChannels;

        async ValueTask<ChannelAliasMap> GetChannelAliasMap(object alias)
        {
            var map = knownAliasMaps.ForAlias(alias);
            if (map != null) return map; // use cached info

            var loadChannel = alias is ChannelId id ? youtube.Client.Channels.GetAsync(id, cancellation)
                : alias is ChannelHandle handle ? youtube.Client.Channels.GetByHandleAsync(handle, cancellation)
                : alias is UserName user ? youtube.Client.Channels.GetByUserAsync(user, cancellation)
                : alias is ChannelSlug slug ? youtube.Client.Channels.GetBySlugAsync(slug, cancellation)
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