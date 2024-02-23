using SubTubular.Extensions;

namespace SubTubular;

public abstract class CommandScope
{
    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.</summary>
    internal abstract string Identify();

    internal abstract IEnumerable<string> Describe();

    /// <summary>A collection of validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    internal readonly List<ValidationResult> Validated = new();
    internal bool IsValid => Validated.All(v => v.IsRemoteValidated);
    internal bool IsPrevalidated => Validated.Any();
    internal ValidationResult SingleValidated => Validated.Single();

    internal void AddPrevalidated(string id, string url) =>
        Validated.Add(new ValidationResult { Id = id, Url = url });

    internal void AddPrevalidated(string alias, object[] wellStructuredAliases) =>
        Validated.Add(new ValidationResult { Id = alias, WellStructuredAliases = wellStructuredAliases });

    internal string[] GetValidatedIds() => Validated.Select(v => v.Id).ToArray();
    internal string GetValidatedId() => GetValidatedIds().Single();

    /*internal IEnumerable<object> GetWellStructuredAliases()
        => Validated.Select(v => v.WellStructuredAliases).WithValue().SelectMany(aliases => aliases);*/

    internal sealed class ValidationResult
    {
        // pre-validation
        /// <summary>The validated identifier of the <see cref="CommandScope"/>.</summary>
        internal required string Id { get; set; }
        internal string? Url { get; set; }
        internal object[]? WellStructuredAliases { get; set; }

        // remote validation
        internal string? Title { get; set; }
        internal Video? Video { get; set; }
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

    internal Video? Video { get; set; }

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
    internal Playlist? Playlist { get; set; }

    // public options
    public ushort Top { get; } = top;
    public float CacheHours { get; } = cacheHours;

    internal override IEnumerable<string> Describe()
    {
        throw new NotImplementedException();
    }
}

public class PlaylistScope(string idOrUrl, ushort top, float cacheHours) : PlaylistLikeScope(top, cacheHours)
{
    internal const string StorageKeyPrefix = "playlist ";
    protected override string KeyPrefix => StorageKeyPrefix;
    public string IdOrUrl { get; } = idOrUrl;
}

public class ChannelScope(string alias, ushort top, float cacheHours) : PlaylistLikeScope(top, cacheHours)
{
    internal const string StorageKeyPrefix = "channel ";
    protected override string KeyPrefix => StorageKeyPrefix;
    public string Alias { get; } = alias;
}
