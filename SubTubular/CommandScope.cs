using SubTubular.Extensions;

namespace SubTubular;

public abstract class CommandScope
{
    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.</summary>
    internal abstract string Identify();

    internal abstract IEnumerable<string> Describe();

    /// <summary>A collection of validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    private List<ValidationResult> validated = new();
    public bool IsValid => validated.All(v => v.IsRemoteValidated);
    public bool IsPrevalidated => validated.Any();
    internal void AddPrevalidated(string id, string url, string? title = null) =>
        validated.Add(new ValidationResult { Id = id, Url = url, Title = title });

    internal void AddPrevalidated(string alias, object[] wellStructuredAliases) =>
        validated.Add(new ValidationResult { Id = alias, WellStructuredAliases = wellStructuredAliases });

    internal string[] GetValidatedIds() => validated.Select(v => v.Id).ToArray();
    internal string GetValidatedId() => GetValidatedIds().Single();

    internal IEnumerable<object> GetWellStructuredAliases()
        => validated.Select(v => v.WellStructuredAliases).WithValue().SelectMany(aliases => aliases);

    internal sealed class ValidationResult
    {
        /// <summary>The validated identifier of the <see cref="CommandScope"/>.</summary>
        internal required string Id { get; set; }
        internal string? Title { get; set; }
        internal string? Url { get; set; }
        internal object[]? WellStructuredAliases { get; set; }
        internal bool IsRemoteValidated { get; set; }
    }
}

internal static class ScopeExtensions
{
    internal static IEnumerable<T> GetValid<T>(this IEnumerable<T> scopes) where T : CommandScope
        => scopes.Where(s => s.IsValid);
}

public class VideosScope(IEnumerable<string> videos) : CommandScope
{
    /// <summary>Input video IDs or URLs.</summary>
    public IEnumerable<string> Videos { get; } = videos;

    internal override string Identify()
    {
        IEnumerable<string> ids = GetValidatedIds();
        if (!ids.Any()) ids = Videos;
        return "videos " + ids.Join(" ");
    }

    internal override IEnumerable<string> Describe()
    {
        throw new NotImplementedException();
    }
}

public abstract class PlaylistLikeScope(ushort top, float cacheHours) : CommandScope
{
    #region internal API
    /// <summary>The prefix for the <see cref="StorageKey"/>.</summary>
    protected abstract string KeyPrefix { get; }

    /// <summary>A unique identifier for the storing this <see cref="PlaylistLikeScope"/>,
    /// capturing its type and <see cref="CommandScope.GetValidatedIds"/>.</summary>
    internal string StorageKey => KeyPrefix + GetValidatedId();

    internal override string Identify() => StorageKey;
    #endregion

    // public options
    public ushort Top { get; } = top;
    public float CacheHours { get; } = cacheHours;

    internal override IEnumerable<string> Describe()
    {
        throw new NotImplementedException();
    }
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
    protected override string KeyPrefix => StorageKeyPrefix;
    public string Alias { get; } = alias;
}
