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
        scope.ValidAliases = PrevalidateChannelAlias(scope.Alias);

        return scope.ValidAliases.HasAny() ? null : scope.Alias; // return invalid
    }

    private static string? Prevalidate(PlaylistScope scope)
    {
        var id = PlaylistId.TryParse(scope.IdOrUrl);
        scope.ValidId = id;
        scope.ValidUrls = ["https://www.youtube.com/playlist?list=" + id];
        return id == null ? scope.IdOrUrl : null; // return invalid
    }

    private static IEnumerable<string> Prevalidate(VideosScope? scope)
    {
        if (scope == null) return [];
        var idsToValid = scope.Videos.ToDictionary(id => id, id => VideoId.TryParse(id.Trim('"'))?.ToString());
        var validIds = idsToValid.Where(pair => pair.Value != null).Select(pair => pair.Value!).ToArray();
        scope.ValidIds = validIds;
        scope.ValidUrls = validIds.Select(Youtube.GetVideoUrl).ToArray();
        return scope.Videos.Except(validIds); // return invalid
    }

    public static async Task RemoteValidateChannelAsync(ChannelScope channel, YoutubeClient youtube, DataStore dataStore, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        // load cached info about which channel aliases map to which channel IDs and which channel IDs are accessible
        var knownAliasMaps = await ChannelAliasMap.LoadList(dataStore) ?? new List<ChannelAliasMap>();

        // remembers whether knownAliasMaps was changed across multiple calls of GetChannelIdMap
        var knownAliasMapsUpdated = false;

        /*  generate tasks checking which of the validAliases are accessible
            (via knownAliasMaps cache or HTTP request) and execute them in parrallel */
        var (matchingChannels, maybeExceptions) = await ValueTasks.WhenAll(channel.ValidAliases!.Select(GetChannelAliasMap));

        // cache accessibility of channel IDs and aliases locally to avoid subsequent HTTP requests
        if (knownAliasMapsUpdated) await ChannelAliasMap.SaveList(knownAliasMaps, dataStore);

        #region rethrow unexpected exceptions
        var exceptions = maybeExceptions.Where(ex => ex is not null).ToArray();

        if (exceptions.Length > 0) throw new AggregateException(
            $"Unexpected errors identifying channel '{channel.Alias}'.", exceptions);
        #endregion

        #region throw input exceptions if Alias matches none or multiple accessible channels
        var indentifiedChannels = matchingChannels.Where(map => map.ChannelId != null).ToArray();
        if (indentifiedChannels.Length == 0) throw new InputException($"Channel '{channel.Alias}' could not be found.");

        var distinctChannels = indentifiedChannels.DistinctBy(map => map.ChannelId);

        if (distinctChannels.Count() > 1) throw new InputException($"Channel alias '{channel.Alias}' is ambiguous:"
            + Environment.NewLine + indentifiedChannels.Select(map =>
            {
                var validUrl = Youtube.GetChannelUrl(channel.ValidAliases!.Single(id => id.GetType().Name == map.Type));
                var channelUrl = Youtube.GetChannelUrl((ChannelId)map.ChannelId!);
                return $"{validUrl} points to channel {channelUrl}";
            })
            .Join(Environment.NewLine)
            + Environment.NewLine + "Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.");
        #endregion

        var identifiedMap = distinctChannels.Single();
        channel.ValidId = identifiedMap.ChannelId;
        channel.ValidUrls = new[] { Youtube.GetChannelUrl((ChannelId)identifiedMap.ChannelId!) };

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