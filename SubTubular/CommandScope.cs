using SubTubular.Extensions;

namespace SubTubular;

public abstract class CommandScope
{
    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.</summary>
    internal abstract string Describe();

    /// <summary>A collection of validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    private readonly List<ValidationResult> validated = new();

    internal bool IsValid => validated.All(v => v.IsRemoteValidated);
    internal bool IsPrevalidated => validated.Count > 0;
    internal ValidationResult SingleValidated => validated.Single();

    internal void AddPrevalidated(string id, string url) =>
        validated.Add(new ValidationResult { Id = id, Url = url });

    internal void AddPrevalidated(string alias, object[] wellStructuredAliases) =>
        validated.Add(new ValidationResult { Id = alias, WellStructuredAliases = wellStructuredAliases });

    /// <summary>Returns all pre-validated or validated <see cref="ValidationResult.Id"/> depending on <see cref="IsValid"/>.</summary>
    internal string[] GetValidatedIds() => validated.Select(v => v.Id).ToArray();

    internal string GetValidatedId() => GetValidatedIds().Single();

    internal sealed class ValidationResult
    {
        // pre-validation, checking input syntax
        /// <summary>The validated identifier of the <see cref="CommandScope"/>.</summary>
        internal required string Id { get; set; }

        internal string? Url { get; set; }

        /// <summary>Syntactically correct interpretations of <see cref="ChannelScope.Alias"/>
        /// returned by <see cref="CommandValidator.PrevalidateChannelAlias(string)"/>.
        /// For <see cref="ChannelScope"/>s only.</summary>
        internal object[]? WellStructuredAliases { get; set; }

        // proper validation, including loading from YouTube if required.
        internal bool IsRemoteValidated => Url != null;
    }
}

internal static class ScopeExtensions
{
    internal static IEnumerable<T> GetValid<T>(this IEnumerable<T> scopes) where T : CommandScope
        => scopes.Where(s => s.IsValid);
}

public class VideosScope : CommandScope
{
    /// <summary>Input video IDs or URLs.</summary>
    public IEnumerable<string> Videos { get; set; }

    public VideosScope(IEnumerable<string> videos) => Videos = videos;

    /// <inheritdoc />
    internal override string Describe()
    {
        IEnumerable<string> ids = GetValidatedIds(); // pre-validated IDs
        if (!ids.Any()) ids = Videos; // or the unvalidated inputs
        return "videos " + ids.Join(" "); // and join them
    }
}

public abstract class PlaylistLikeScope(string alias, ushort top, float cacheHours) : CommandScope
{
    #region internal API
    /// <summary>The prefix for the <see cref="StorageKey"/>.</summary>
    protected abstract string KeyPrefix { get; }

    /// <inheritdoc />
    internal override string Describe() => StorageKey;

    /// <summary>A unique identifier for the storing this <see cref="PlaylistLikeScope"/>,
    /// capturing its type and <see cref="CommandScope.GetValidatedIds"/>.</summary>
    internal string StorageKey => KeyPrefix + GetValidatedId();
    #endregion

    // public options
    public ushort Top { get; } = top;
    public string Alias { get; set; } = alias;
    public float CacheHours { get; } = cacheHours;
}

public class PlaylistScope(string alias, ushort top, float cacheHours) : PlaylistLikeScope(alias, top, cacheHours)
{
    internal const string StorageKeyPrefix = "playlist ";
    protected override string KeyPrefix => StorageKeyPrefix;
}

public class ChannelScope(string alias, ushort top, float cacheHours) : PlaylistLikeScope(alias, top, cacheHours)
{
    internal const string StorageKeyPrefix = "channel ";
    protected override string KeyPrefix => StorageKeyPrefix;
}
