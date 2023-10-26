namespace SubTubular;

internal abstract class CommandScope
{
    internal IEnumerable<string> ValidUrls { get; set; }
    internal abstract string Describe();
}

internal class VideosScope : CommandScope
{
    public IEnumerable<string> Videos { get; set; }

    internal string[] ValidIds { get; set; }
    internal override string Describe() => "videos " + ValidIds.Join(" ");
}

internal abstract class PlaylistLikeScope : CommandScope
{
    protected internal string ValidId { get; set; }
    protected abstract string KeyPrefix { get; }
    internal string StorageKey => KeyPrefix + ValidId;

    public ushort Top { get; set; }
    public IEnumerable<OrderOptions> OrderBy { get; set; }
    public float CacheHours { get; set; }

    internal override string Describe() => StorageKey;

    /// <summary>Mutually exclusive <see cref="OrderOptions"/>.</summary>
    internal static OrderOptions[] Orders = new[] { OrderOptions.uploaded, OrderOptions.score };

    /// <summary><see cref="Orders"/> and modifiers.</summary>
    public enum OrderOptions { uploaded, score, asc }
}

internal class PlaylistScope : PlaylistLikeScope
{
    internal const string StorageKeyPrefix = "playlist ";

    public string Playlist { get; set; }
    protected override string KeyPrefix => StorageKeyPrefix;
}

internal class ChannelScope : PlaylistLikeScope
{
    internal const string StorageKeyPrefix = "channel ";

    public string Alias { get; set; }
    protected override string KeyPrefix => StorageKeyPrefix;
    internal object[] ValidAliases { get; set; }
}
