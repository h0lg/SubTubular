using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

public abstract partial class CommandScope
{
    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.
    /// May yield multiple lines.</summary>
    public abstract IEnumerable<string> Describe();

    // used for debugging
    public override string ToString() => Describe().Join(" ");
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

    // for equality comparison of recent commands
    public override int GetHashCode() => Alias.GetHashCode();
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
