using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

public abstract class CommandScope
{
    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.
    /// May yield multiple lines.</summary>
    public abstract IEnumerable<string> Describe();

    /// <summary>A collection of validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    internal readonly List<ValidationResult> Validated = new();

    internal bool IsValid => IsPrevalidated && Validated.All(v => v.IsRemoteValidated);
    internal bool IsPrevalidated => Validated.Count > 0;
    internal ValidationResult SingleValidated => Validated.Single();

    internal void AddPrevalidated(string id, string url) =>
        Validated.Add(new ValidationResult { Id = id, Url = url });

    internal void AddPrevalidated(string alias, object[] wellStructuredAliases) =>
        Validated.Add(new ValidationResult { Id = alias, WellStructuredAliases = wellStructuredAliases });

    /// <summary>Returns all pre-validated or validated <see cref="ValidationResult.Id"/> depending on <see cref="IsValid"/>.</summary>
    internal string[] GetValidatedIds() => Validated.Select(v => v.Id).ToArray();

    internal string GetValidatedId() => GetValidatedIds().Single();

    // used for debugging
    public override string ToString() => Describe().Join(" ");

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
        internal string? Title => Playlist?.Title ?? Video?.Title;
        internal bool IsRemoteValidated => Playlist != null || Video != null;

        /// <summary>For <see cref="VideosScope"/>s only.</summary>
        internal Video? Video { get; set; }

        /// <summary>For <see cref="PlaylistLikeScope"/>s only.</summary>
        internal Playlist? Playlist { get; set; }
    }
}

internal static class ScopeExtensions
{
    internal static IEnumerable<T> GetValid<T>(this IEnumerable<T> scopes) where T : CommandScope
        => scopes.Where(s => s.IsValid);
}

[Serializable]
public class VideosScope : CommandScope, ISerializable
{
    /// <summary>Input video IDs or URLs.</summary>
    public string[] Videos { get; }

    [JsonConstructor]
    public VideosScope(string[] videos)
        => Videos = videos.Select(id => id.Trim()).ToArray();

    public override IEnumerable<string> Describe()
    {
        if (IsValid) // if validated, use titles - one per line
            foreach (var validated in Validated)
                yield return validated.Title!;
        else // otherwise fall back to
        {
            IEnumerable<string> ids = GetValidatedIds(); // pre-validated IDs
            if (!ids.Any()) ids = Videos; // or the unvalidated inputs
            yield return "videos " + ids.Join(" "); // and join them
        }
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        => info.AddValue(nameof(Videos), Videos);
}

public abstract class PlaylistLikeScope : CommandScope
{
    #region internal API
    /// <summary>The prefix for the <see cref="StorageKey"/>.</summary>
    protected abstract string KeyPrefix { get; }

    /// <summary>A unique identifier for the storing this <see cref="PlaylistLikeScope"/>,
    /// capturing its type and <see cref="CommandScope.GetValidatedIds"/>.</summary>
    internal string StorageKey => KeyPrefix + GetValidatedId();
    #endregion

    // public options
    public string Alias { get; set; }
    public ushort Top { get; }
    public float CacheHours { get; }

    protected PlaylistLikeScope(string alias, ushort top, float cacheHours)
    {
        Alias = alias.Trim();
        Top = top;
        CacheHours = cacheHours;
    }

    public override IEnumerable<string> Describe()
    {
        yield return IsValid ? SingleValidated.Playlist!.Title : Alias;
    }
}

[Serializable]
public class PlaylistScope(string alias, ushort top, float cacheHours) : PlaylistLikeScope(alias, top, cacheHours)
{
    internal const string StorageKeyPrefix = "playlist ";
    protected override string KeyPrefix => StorageKeyPrefix;
}

[Serializable]
public class ChannelScope(string alias, ushort top, float cacheHours) : PlaylistLikeScope(alias, top, cacheHours)
{
    internal const string StorageKeyPrefix = "channel ";
    protected override string KeyPrefix => StorageKeyPrefix;
}
