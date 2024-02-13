using SubTubular.Extensions;

namespace SubTubular;

public abstract class CommandScope
{
    private IEnumerable<string>? validUrls;

    /// <summary>A collection of validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    public IEnumerable<string>? ValidUrls
    {
        get { return validUrls; }
        internal set
        {
            validUrls = value;
            IsValid = validUrls.HasAny();
        }
    }

    public bool IsValid { get; private set; }

    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.</summary>
    internal abstract string Describe();
}

internal static class ScopeExtensions
{
    internal static IEnumerable<T> GetValid<T>(this IEnumerable<T> scopes) where T : CommandScope
        => scopes.Where(s => s.IsValid);
}

public class VideosScope : CommandScope
{
    /// <summary>Video IDs or URLs.</summary>
    public IEnumerable<string> Videos { get; set; }

    /// <summary>Validated video IDs from <see cref="Videos"/>.</summary>
    internal string[]? ValidIds { get; set; }

    public VideosScope(IEnumerable<string> videos) => Videos = videos;

    /// <inheritdoc />
    internal override string Describe() => "videos " + (ValidIds ?? Videos).Join(" ");
}

public abstract class PlaylistLikeScope(ushort top, float cacheHours) : CommandScope
{
    #region internal API
    /// <summary>The prefix for the <see cref="StorageKey"/>.</summary>
    protected abstract string KeyPrefix { get; }

    /// <summary>The validated ID for this <see cref="PlaylistLikeScope"/>.</summary>
    protected internal string? ValidId { get; set; }

    /// <summary>A unique identifier for the storing this <see cref="PlaylistLikeScope"/>,
    /// capturing its type and <see cref="ValidId"/>.</summary>
    internal string StorageKey => KeyPrefix + ValidId;

    /// <inheritdoc />
    internal override string Describe() => StorageKey;
    #endregion

    // public options
    public ushort Top { get; } = top;
    public float CacheHours { get; } = cacheHours;
}

public class PlaylistScope(string playlist, ushort top, float cacheHours) : PlaylistLikeScope(top, cacheHours)
{
    internal const string StorageKeyPrefix = "playlist ";
    protected override string KeyPrefix => StorageKeyPrefix;
    public string Playlist { get; } = playlist;
}

public class ChannelScope(string alias, ushort top, float cacheHours) : PlaylistLikeScope(top, cacheHours)
{
    internal const string StorageKeyPrefix = "channel ";

    public string Alias { get; } = alias;
    protected override string KeyPrefix => StorageKeyPrefix;
    internal object[]? ValidAliases { get; set; }
}
