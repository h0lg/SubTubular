using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

[JsonDerivedType(typeof(SearchCommand), "search")]
[JsonDerivedType(typeof(ListKeywords), "list keywords")]
public abstract class OutputCommand
{
    public VideosScope? Videos { get; set; }
    public PlaylistScope[]? Playlists { get; set; }
    public ChannelScope[]? Channels { get; set; }

    public bool SaveAsRecent { get; set; } = true;

    public short OutputWidth { get; set; } = 80;
    public bool OutputHtml { get; set; }
    public string? FileOutputPath { get; set; }
    public Shows? Show { get; set; }

    public bool HasOutputPath(out string? outputPath)
    {
        var fileOutputPath = FileOutputPath?.Trim('"');
        var hasOutputPath = fileOutputPath.IsNonEmpty();

        if (hasOutputPath)
        {
            outputPath = fileOutputPath;
            return true;
        }
        else
        {
            outputPath = null;
            return false;
        }
    }

    internal bool HasPreValidatedScopes() => GetScopes().Any(s => s.IsPrevalidated);

    internal IEnumerable<PlaylistLikeScope> GetPlaylistLikeScopes()
    {
        if (Channels.HasAny()) foreach (var channel in Channels!) yield return channel;
        if (Playlists.HasAny()) foreach (var playlist in Playlists!) yield return playlist;
    }

    private IEnumerable<CommandScope> GetScopes()
    {
        foreach (var playlist in GetPlaylistLikeScopes()) yield return playlist;
        if (Videos != null) yield return Videos;
    }

    public bool AreScopesValid() => GetScopes().All(s => s.IsValid);
    protected string DescribeScopes() => GetScopes().Select(p => p.Describe().Join(" ")).Join(" ");

    /// <summary>Provides a human-readable description of the command, by default <paramref name="withScopes"/>.
    /// This can be used to generate unique file names, but be aware that the returned description is not filename-safe.</summary>
    public abstract string Describe(bool withScopes = true);

    // for comparing in recent command list
    public override int GetHashCode()
        => new HashCode()
            .AddOrdered(Channels.AsHashCodeSet())
            .AddOrdered(Playlists.AsHashCodeSet())
            .AddOrdered(Videos?.Videos.AsHashCodeSet() ?? [])
            .ToHashCode();

    // for comparing in recent command list
    public override bool Equals(object? obj)
        => obj != null && obj.GetType() == GetType() && obj.GetHashCode() == GetHashCode();

    public enum Shows { file, folder }
}

public sealed class SearchCommand : OutputCommand
{
    public const string Description = "Search the subtitles and metadata of videos in the given scopes.";

    public string? Query { get; set; }
    public ushort Padding { get; set; }

    // default to ordering by highest score which is probably most useful for most purposes
    public IEnumerable<OrderOptions> OrderBy { get; set; } = [OrderOptions.score];

    public override string Describe(bool withScopes = true)
        => $"searching for {Query} in" + (withScopes ? " " + DescribeScopes() : null);

    // for comparing in recent command list
    public override int GetHashCode() => HashCode.Combine(Query, base.GetHashCode());

    /// <summary>Mutually exclusive <see cref="OrderOptions"/>.</summary>
    internal static OrderOptions[] Orders = [OrderOptions.uploaded, OrderOptions.score];

    /// <summary><see cref="Orders"/> and modifiers.</summary>
    public enum OrderOptions { uploaded, score, asc }
}

public sealed class ListKeywords : OutputCommand
{
    public const string Description = "List the keywords of videos in the given scopes.";

    public override string Describe(bool withScopes = true)
        => "listing keywords in" + (withScopes ? " " + DescribeScopes() : null);
}
