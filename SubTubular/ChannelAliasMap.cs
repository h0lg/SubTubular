namespace SubTubular;

/// <summary>Maps valid channel aliases by <see cref="Type"/> and <see cref="Value"/>
/// to an accessible <see cref="ChannelId"/> or null if none was found.</summary>
[Serializable]
public sealed class ChannelAliasMap
{
    internal const string StorageKey = "known channel alias maps";

    public required string Type { get; set; }
    public required string Value { get; set; }
    public string? ChannelId { get; set; }

    internal static (string, string) GetTypeAndValue(object alias) => (alias.GetType().Name, alias.ToString()!);

    internal static async Task<HashSet<ChannelAliasMap>> LoadList(DataStore dataStore)
        => await dataStore.GetAsync<HashSet<ChannelAliasMap>>(StorageKey) ?? [];

    internal static async Task SaveList(HashSet<ChannelAliasMap> maps, DataStore dataStore)
        => await dataStore.SetAsync(StorageKey, maps);
}
