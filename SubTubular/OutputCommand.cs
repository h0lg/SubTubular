using System.Text.Json.Serialization;
using SubTubular.Extensions;
using SubTubular.Shell;

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

    public IEnumerable<CommandScope> GetScopes()
    {
        foreach (var playlist in GetPlaylistLikeScopes()) yield return playlist;
        if (Videos != null) yield return Videos;
    }

    public bool AreScopesValid() => GetScopes().All(s => s.IsValid);
    protected string DescribeScopes() => GetScopes().Select(p => p.Describe().Join(" ")).Join(" ");

    /// <summary>Forwards the <see cref="CommandScope.Notify"/> on all <see cref="GetScopes"/>
    /// for notifications during their async processing to the supplied <paramref name="notify"/>.
    /// Takes (in parameter order) 1. the scope the a message originated from,
    /// 2. the title, 3. an optional body and 4. optional exceptions that occurred.</summary>
    public void OnScopeNotification(Action<CommandScope, string, string?, Exception[]?> notify)
    {
        foreach (var scope in GetScopes())
            scope.Notify = (title, message, errors) => notify(scope, title, message, errors);
    }

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

    protected string FormatShellCommand(string action, string? extraParameters = null)
    {
        string shellCmd = $"{AssemblyInfo.Name}.exe {action}";

        if (Channels?.Length > 0) shellCmd += $" {Scopes.channels} {Channels.Select(c => c.Alias).Join(" ")}";
        if (Playlists?.Length > 0) shellCmd += $" {Scopes.playlists} {Playlists.Select(pl => pl.Alias).Join(" ")}";
        if (Videos != null) shellCmd += $" {Scopes.videos} {Videos.Videos.Join(" ")}";

        if (extraParameters != null) shellCmd += extraParameters;
        var playlistLike = GetPlaylistLikeScopes().ToArray();

        if (playlistLike.Length != 0)
        {
            var skip = playlistLike.Max(s => s.Skip);
            var take = playlistLike.Max(s => s.Take);
            var cacheHours = playlistLike.Max(s => s.CacheHours);
            if (skip > 0) shellCmd += $" {Args.skip} {skip}";
            if (take > 0) shellCmd += $" {Args.take} {take}";
            if (cacheHours > 0) shellCmd += $" {Args.cacheHours} {cacheHours}";
        }

        return shellCmd;
    }

    public abstract string ToShellCommand();

    public enum Shows { file, folder }
}

public sealed class SearchCommand : OutputCommand
{
    public const string Description = "Search the subtitles and metadata of videos in the given scopes.";

    public static readonly string[] QueryHints = [
        @"Quote ""multi-word phrases"".",
        "Single words are matched exactly by default, ?fuzzy or with wild cards for s%ngle and multi* letters.",
        @"Combine multiple & terms | ""phrases or queries"" using AND '&' and OR '|'.",
        "Use ( brackets | for ) & ( complex | expressions ).",
        $"You can restrict your search to the video '{nameof(Video.Title)}', '{nameof(Video.Description)}',"
            + $@" '{nameof(Video.Keywords)}' and/or '{nameof(CaptionTrack.Captions)}'; e.g. '{nameof(Video.Title)}=""click bait""'."];

    public string? Query { get; set; }
    public ushort Padding { get; set; }

    // default to ordering by highest score which is probably most useful for most purposes
    public IEnumerable<OrderOptions> OrderBy { get; set; } = [OrderOptions.score];

    public override string Describe(bool withScopes = true)
        => $"searching for {Query} in" + (withScopes ? " " + DescribeScopes() : null);

    // for comparing in recent command list
    public override int GetHashCode() => HashCode.Combine(Query, base.GetHashCode());

    public static string GetQueryHint() => QueryHints.Join(" ");

    public override string ToShellCommand()
        => FormatShellCommand(Actions.search, $" {Args.@for} {Query} {Args.pad} {Padding} {Args.orderBy} {OrderBy.Select(o => o.ToString()).Join(" ")}");

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

    public override string ToShellCommand() => FormatShellCommand(Actions.listKeywords);
}
