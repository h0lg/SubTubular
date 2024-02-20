using SubTubular.Extensions;

namespace SubTubular;

public abstract class OutputCommand
{
    public VideosScope? Videos { get; set; }
    public PlaylistScope[]? Playlists { get; set; }
    public ChannelScope[]? Channels { get; set; }

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

    internal bool HasPreValidatedScopes() => Videos?.ValidIds.HasAny() == true
        || Playlists?.Any(pl => pl.ValidId != null) == true
        || Channels?.Any(ch => ch.ValidAliases.HasAny()) == true;

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

    internal IEnumerable<CommandScope> GetValidScopes() => GetScopes().GetValid();
    protected string DescribeValidScopes() => GetValidScopes().Select(p => p.Describe()).Join(" ");
    public abstract string Describe();

    public enum Shows { file, folder }
}

public sealed class SearchCommand : OutputCommand
{
    public string? Query { get; set; }
    public ushort Padding { get; set; }

    // default to ordering by highest score which is probably most useful for most purposes
    public IEnumerable<OrderOptions> OrderBy { get; set; } = [OrderOptions.score];

    public override string Describe() => "searching " + DescribeValidScopes() + " for " + Query;

    /// <summary>Mutually exclusive <see cref="OrderOptions"/>.</summary>
    internal static OrderOptions[] Orders = [OrderOptions.uploaded, OrderOptions.score];

    /// <summary><see cref="Orders"/> and modifiers.</summary>
    public enum OrderOptions { uploaded, score, asc }
}

public sealed class ListKeywords : OutputCommand
{
    public override string Describe() => "listing keywords in " + DescribeValidScopes();
}
