using SubTubular.Extensions;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace SubTubular;

public static class CommandValidator
{
    // see Lifti.Querying.QueryTokenizer.ParseQueryTokens()
    private static readonly char[] controlChars = ['*', '%', '|', '&', '"', '~', '>', '?', '(', ')', '=', ','];

    public static void ValidateSearchCommand(SearchCommand command)
    {
        ValidateCommandScope(command);

        if (command.Query.IsNullOrWhiteSpace()) throw new InputException(
            $"The {nameof(SearchCommand.Query).ToLower()} is empty.");

        if (command.Query.HasAny() && command.Query!.All(c => controlChars.Contains(c))) throw new InputException(
            $"The {nameof(SearchCommand.Query).ToLower()} contains nothing but control characters."
            + " That'll stay unsupported unless you come up with a good reason for why it should be."
            + $" If you can, leave it at {AssemblyInfo.IssuesUrl} .");

        if (command.OrderBy.Intersect(SearchCommand.Orders).Count() > 1) throw new InputException(
            $"You may order by either '{nameof(SearchCommand.OrderOptions.score)}' or '{nameof(SearchCommand.OrderOptions.uploaded)}' (date), but not both.");
    }

    public static void ValidateCommandScope(OutputCommand command)
    {
        var errors = (new[]
        {
            TryGetScopeError(command.Playlists?.Select(Validate), "{0} are not valid playlist IDs or URLs."),
            TryGetScopeError(command.Channels?.Select(Validate), "{0} are not valid channel handles, slugs, user names or IDs."),
            TryGetScopeError(Validate(command.Videos), "{0} are not valid video IDs or URLs.")
        }).WithValue().ToArray();

        if (errors.Length != 0) throw new InputException(errors.Join(Environment.NewLine));
        if (!command.HasPreValidatedScopes()) throw new InputException("No valid scope.");
    }

    internal static object[] ValidateChannelAlias(string alias)
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

    private static string? Validate(ChannelScope scope)
    {
        /*  validate Alias locally to throw eventual InputException,
            but store result for RemoteValidateChannelAsync */
        scope.ValidAliases = ValidateChannelAlias(scope.Alias);

        return scope.ValidAliases.HasAny() ? null : scope.Alias; // return invalid
    }

    private static string? Validate(PlaylistScope scope)
    {
        var id = PlaylistId.TryParse(scope.Playlist);
        scope.ValidId = id;
        scope.ValidUrls = ["https://www.youtube.com/playlist?list=" + id];
        return id == null ? scope.Playlist : null; // return invalid
    }

    private static IEnumerable<string> Validate(VideosScope? scope)
    {
        if (scope == null) return [];
        var idsToValid = scope.Videos.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"'))?.ToString());
        var validIds = idsToValid.Where(pair => pair.Value != null).Select(pair => pair.Value!).ToArray();
        scope.ValidIds = validIds;
        scope.ValidUrls = validIds.Select(Youtube.GetVideoUrl).ToArray();
        return scope.Videos.Except(validIds); // return invalid
    }

    public static async Task RemoteValidateChannelAsync(ChannelScope command, YoutubeClient youtube, DataStore dataStore, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        // load cached info about which channel aliases map to which channel IDs and which channel IDs are accessible
        var knownAliasMaps = await ChannelAliasMap.LoadList(dataStore) ?? new List<ChannelAliasMap>();

        // remembers whether knownAliasMaps was changed across multiple calls of GetChannelIdMap
        var knownAliasMapsUpdated = false;

        /*  generate tasks checking which of the validAliases are accessible
            (via knownAliasMaps cache or HTTP request) and execute them in parrallel */
        var (aliasMaps, maybeExceptions) = await ValueTasks.WhenAll(command.ValidAliases!.Select(GetChannelAliasMap));

        // cache accessibility of channel IDs and aliases locally to avoid subsequent HTTP requests
        if (knownAliasMapsUpdated) await ChannelAliasMap.SaveList(knownAliasMaps, dataStore);

        #region rethrow unexpected exceptions
        var exceptions = maybeExceptions.Where(ex => ex is not null).ToArray();

        if (exceptions.Length > 0) throw new AggregateException(
            $"Unexpected errors identifying channel '{command.Alias}'.", exceptions);
        #endregion

        #region throw input exceptions if Alias matches none or multiple accessible channels
        var accessibleMaps = aliasMaps.Where(map => map.ChannelId != null).ToArray();
        if (accessibleMaps.Length == 0) throw new InputException($"Channel '{command.Alias}' could not be found.");

        var distinct = accessibleMaps.DistinctBy(map => map.ChannelId);

        if (distinct.Count() > 1) throw new InputException($"Channel alias '{command.Alias}' is ambiguous:"
            + Environment.NewLine + accessibleMaps.Select(map =>
            {
                var validUrl = Youtube.GetChannelUrl(command.ValidAliases!.Single(id => id.GetType().Name == map.Type));
                var channelUrl = Youtube.GetChannelUrl((ChannelId)map.ChannelId!);
                return $"{validUrl} points to channel {channelUrl}";
            })
            .Join(Environment.NewLine)
            + Environment.NewLine + "Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.");
        #endregion

        var identifiedMap = distinct.Single();
        command.ValidId = identifiedMap.ChannelId;
        command.ValidUrls = new[] { Youtube.GetChannelUrl((ChannelId)identifiedMap.ChannelId!) };

        async ValueTask<ChannelAliasMap> GetChannelAliasMap(object alias)
        {
            var map = knownAliasMaps.ForAlias(alias);
            if (map != null) return map; // use cached info

            var loadChannel = alias is ChannelHandle handle ? youtube.Channels.GetByHandleAsync(handle, cancellation)
                : alias is ChannelSlug slug ? youtube.Channels.GetBySlugAsync(slug, cancellation)
                : alias is UserName user ? youtube.Channels.GetByUserAsync(user, cancellation)
                : alias is ChannelId id ? youtube.Channels.GetAsync(id, cancellation)
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