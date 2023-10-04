using CommandLine;
using YoutubeExplode;
using YoutubeExplode.Channels;

namespace SubTubular;

[Verb("search-channel", aliases: new[] { "channel", "c" },
    HelpText = "Searches the videos in a channel's Uploads playlist."
    + $" This is a glorified '{SearchPlaylist.Command}'.")]
internal sealed class SearchChannel : SearchPlaylistCommand, RemoteValidated
{
    internal const string StorageKeyPrefix = "channel ";

    [Value(0, MetaName = "channel", Required = true,
        HelpText = "The channel ID, handle, slug, user name or a URL for either of those.")]
    public string Alias { get; set; }

    protected override string KeyPrefix => StorageKeyPrefix;

    #region VALIDATION
    private object[] validAliases; // stores validated alias between local and remote validation

    internal override void Validate()
    {
        base.Validate();

        /*  validate Alias locally to throw eventual InputException,
            but store result for RemoteValidateAsync */
        validAliases = ValidateAlias(Alias);
    }

    internal static object[] ValidateAlias(string alias)
    {
        var handle = ChannelHandle.TryParse(alias);
        var slug = ChannelSlug.TryParse(alias);
        var user = UserName.TryParse(alias);
        var id = ChannelId.TryParse(alias);
        var valid = new object[] { handle, slug, user, id }.Where(id => id != null).ToArray();

        if (valid.Length == 0) throw new InputException(
            $"'{alias}' is not a valid channel handle, slug, user name or channel ID.");

        return valid;
    }

    public async Task RemoteValidateAsync(YoutubeClient youtube, DataStore dataStore, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        // load cached info about which channel aliases map to which channel IDs and which channel IDs are accessible
        var knownAliasMaps = await ChannelAliasMap.LoadList(dataStore) ?? new List<ChannelAliasMap>();

        // remembers whether knownAliasMaps was changed across multiple calls of GetChannelIdMap
        var knownAliasMapsUpdated = false;

        /*  generate tasks checking which of the validAliases are accessible
            (via knownAliasMaps cache or HTTP request) and execute them in parrallel */
        var (aliasMaps, maybeExceptions) = await ValueTasks.WhenAll(validAliases.Select(GetChannelAliasMap));

        // cache accessibility of channel IDs and aliases locally to avoid subsequent HTTP requests
        if (knownAliasMapsUpdated) await ChannelAliasMap.SaveList(knownAliasMaps, dataStore);

        #region rethrow unexpected exceptions
        var exceptions = maybeExceptions.Where(ex => ex is not null).ToArray();

        if (exceptions.Length > 0) throw new AggregateException(
            $"Unexpected errors identifying channel '{Alias}'.", exceptions);
        #endregion

        #region throw input exceptions if Alias matches none or multiple accessible channels
        var accessibleMaps = aliasMaps.Where(map => map.ChannelId != null).ToArray();
        if (accessibleMaps.Length == 0) throw new InputException($"Channel '{Alias}' could not be found.");

        var distinct = accessibleMaps.DistinctBy(map => map.ChannelId);

        if (distinct.Count() > 1) throw new InputException($"Channel alias '{Alias}' is ambiguous:"
            + Environment.NewLine + accessibleMaps.Select(map =>
            {
                var validUrl = Youtube.GetChannelUrl(validAliases.Single(id => id.GetType().Name == map.Type));
                var channelUrl = Youtube.GetChannelUrl((ChannelId)map.ChannelId);
                return $"{validUrl} points to channel {channelUrl}";
            })
            .Join(Environment.NewLine)
            + Environment.NewLine + "Specify the unique channel ID or full handle URL, custom/slug URL or user URL to disambiguate the channel.");
        #endregion

        var identifiedMap = distinct.Single();
        ValidId = identifiedMap.ChannelId;
        ValidUrls = new[] { Youtube.GetChannelUrl((ChannelId)identifiedMap.ChannelId) };

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
            catch (HttpRequestException ex)
            {
                if (ex.IsNotFound()) map.ChannelId = null;
                else throw; // rethrow to raise assumed transient error
            }

            knownAliasMaps.Add(map);
            knownAliasMapsUpdated = true;
            return map;
        }
    }
    #endregion
}