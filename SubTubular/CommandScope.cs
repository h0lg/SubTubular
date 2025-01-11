using System.Text.Json.Serialization;
using SubTubular.Extensions;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace SubTubular;

public abstract partial class CommandScope
{
    /// <summary>Provides a description of the scope for <see cref="OutputCommand.Describe"/>.
    /// May yield multiple lines.</summary>
    public abstract IEnumerable<string> Describe(bool inDetail = true);

    // used for debugging
    public override string ToString() => Describe().Join(" ");
}

[method: JsonConstructor]
public class VideosScope(List<string> videos) : CommandScope
{
    /// <summary>Input video IDs or URLs.</summary>
    public List<string> Videos { get; } = videos.Select(id => id.Trim()).ToList();

    public static string? TryParseId(string id) => VideoId.TryParse(id.Trim('"'))?.ToString();
    public override bool RequiresValidation() => Videos.Except(GetRemoteValidated().Ids()).Any();

    /// <summary>Returns inputs that pre-validate as video ID or URL but don't remote-validate.</summary>
    public IEnumerable<string> GetRemoteInvalidatedIds()
        => Progress.Videos?.Where(pair => pair.Value == VideoList.Status.invalid).Select(pair => pair.Key) ?? [];

    public void Remove(string video)
    {
        RemoveInput(video);
        Progress.Videos?.Remove(video);
    }

    internal void Invalidate(string video)
    {
        RemoveInput(video);
        Report(video, VideoList.Status.invalid);
    }

    private void RemoveInput(string video)
    {
        Videos.Remove(video); // don't try to re-validate them
        Validated.RemoveAll(v => v.Id == video); // clear maybe failed validation
    }

    public override IEnumerable<string> Describe(bool inDetail = true)
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

    /*void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        => info.AddValue(nameof(Videos), Videos);*/
}

public abstract class PlaylistLikeScope : CommandScope
{
    #region internal API
    /// <summary>The prefix for the <see cref="StorageKey"/>.</summary>
    protected abstract string KeyPrefix { get; }

    /// <summary>A unique identifier for the storing this <see cref="PlaylistLikeScope"/>,
    /// capturing its type and <see cref="CommandScope.ValidationResult.Id"/>.</summary>
    internal string StorageKey => KeyPrefix + SingleValidated.Id;
    #endregion

    // public options
    public string Alias { get; set; }
    public ushort Skip { get; set; }
    public ushort Take { get; set; }
    public float CacheHours { get; set; }

    protected PlaylistLikeScope(string alias, ushort skip, ushort take, float cacheHours)
    {
        Alias = alias.Trim();
        Skip = skip;
        Take = take;
        CacheHours = cacheHours;
    }

    public override IEnumerable<string> Describe(bool inDetail = true)
    {
        var identifier = IsValid ? SingleValidated.Playlist!.Title : Alias;

        if (!inDetail) yield return identifier;
        else yield return $"{identifier} ({Skip + 1} - {Skip + Take + 1})";
    }

    public override bool RequiresValidation() => Alias.IsNonWhiteSpace() && !IsValid;

    // for equality comparison of recent commands
    public override int GetHashCode() => Alias.GetHashCode();
}

public class PlaylistScope(string alias, ushort skip, ushort take, float cacheHours) : PlaylistLikeScope(alias, skip, take, cacheHours)
{
    public const string StorageKeyPrefix = "playlist ";
    protected override string KeyPrefix => StorageKeyPrefix;
    public static string? TryParseId(string alias) => PlaylistId.TryParse(alias);
}

public class ChannelScope(string alias, ushort skip, ushort take, float cacheHours) : PlaylistLikeScope(alias, skip, take, cacheHours)
{
    public const string StorageKeyPrefix = "channel ";
    protected override string KeyPrefix => StorageKeyPrefix;
}
