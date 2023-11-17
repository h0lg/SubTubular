using SubTubular.Extensions;

namespace SubTubular;

internal abstract class CommandScope
{
    /// <summary>A collection of validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    internal IEnumerable<string> ValidUrls { get; set; }

    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.</summary>
    internal abstract string Describe();
}

internal class VideosScope : CommandScope
{
    /// <summary>Video IDs or URLs.</summary>
    public IEnumerable<string> Videos { get; set; }

    /// <summary>Validated video IDs from <see cref="Videos"/>.</summary>
    internal string[] ValidIds { get; set; }

    /// <inheritdoc />
    internal override string Describe() => "videos " + ValidIds.Join(" ");
}

internal abstract class PlaylistLikeScope : CommandScope
{
    #region internal API
    /// <summary>The prefix for the <see cref="StorageKey"/>.</summary>
    protected abstract string KeyPrefix { get; }

    /// <summary>The validated ID for this <see cref="PlaylistLikeScope"/>.</summary>
    protected internal string ValidId { get; set; }

    /// <summary>A unique identifier for the storing this <see cref="PlaylistLikeScope"/>,
    /// capturing its type and <see cref="ValidId"/>.</summary>
    internal string StorageKey => KeyPrefix + ValidId;

    /// <inheritdoc />
    internal override string Describe() => StorageKey;
    #endregion

    // public options
    public ushort Top { get; set; }
    public IEnumerable<OrderOptions> OrderBy { get; set; }
    public float CacheHours { get; set; }


    /// <summary>Mutually exclusive <see cref="OrderOptions"/>.</summary>
    internal static OrderOptions[] Orders = new[] { OrderOptions.uploaded, OrderOptions.score };

    /// <summary><see cref="Orders"/> and modifiers.</summary>
    public enum OrderOptions { uploaded, score, asc }
}

internal class PlaylistScope : PlaylistLikeScope
{
    internal const string StorageKeyPrefix = "playlist ";
    protected override string KeyPrefix => StorageKeyPrefix;

    public string Playlist { get; set; }
}

internal class ChannelScope : PlaylistLikeScope
{
    internal const string StorageKeyPrefix = "channel ";

    public string Alias { get; set; }
    protected override string KeyPrefix => StorageKeyPrefix;
    internal object[] ValidAliases { get; set; }
}
