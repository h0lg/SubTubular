using System.Text.Json.Serialization;
using SubTubular.Extensions;
using SubTubular.Shell;

namespace SubTubular;

[JsonDerivedType(typeof(SearchCommand), "search")]
[JsonDerivedType(typeof(ListKeywords), "list keywords")]
public abstract class OutputCommand
{
    public const string ExistingFilesAreOverWritten = " Existing files with the same name will be overwritten.",
        FileOutputPathHint = "Supply either a file or folder path. If the path doesn't contain a file name, the file will be named according to your search parameters.";

    public VideosScope? Videos { get; set; }
    internal bool HasValidVideos => Videos?.IsValid == true;

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

    public bool RequiresRemoteValidation() => !GetScopes().All(s => s.IsValid);
    protected string DescribeScopes() => GetScopes().Select(p => p.Describe().Join(" ")).Join(" ");

    /// <summary>Forwards the <see cref="CommandScope.Notified"/> on all <see cref="GetScopes"/>
    /// for notifications during their async processing to the supplied <paramref name="notify"/>.</summary>
    public void OnScopeNotification(Action<CommandScope, CommandScope.Notification> notify)
    {
        foreach (var scope in GetScopes())
            scope.Notified += (scope, message) => notify((CommandScope)scope!, message);
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
        string shellCmd = $"{AssemblyInfo.ShellExe} {action}";

        if (Channels?.Length > 0) shellCmd += $" {Scopes.channels} {Channels.Select(c => c.Alias).Join(" ")}";
        if (Playlists?.Length > 0) shellCmd += $" {Scopes.playlists} {Playlists.Select(pl => pl.Alias).Join(" ")}";
        if (Videos != null) shellCmd += $" {Scopes.videos} {Videos.Videos.Join(" ")}";

        if (extraParameters != null) shellCmd += extraParameters;
        var playlistLike = GetPlaylistLikeScopes().ToArray();

        if (playlistLike.Length != 0)
        {
            var skip = playlistLike.All(s => s.Skip == default) ? null
                : playlistLike.Select(l => l.Skip.ToString()).Join(" ");

            var take = playlistLike.Select(l => l.Take.ToString()).Join(" ");

            var cacheHours = playlistLike.Select(s => s.CacheHours.ToString()).Join(" ");

            if (skip != null) shellCmd += $" {Args.skip} {skip}";
            shellCmd += $" {Args.take} {take}";
            if (cacheHours != null) shellCmd += $" {Args.cacheHours} {cacheHours}";
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
        @"Combine multiple & terms | ""phrases or queries"" using '&' as logical 'and' and '|' as 'or'.",
        "Use ( brackets | for ) & ( complex | expressions ).",
        "Words can have > order, appear ~ near to each other - or both, even with configurable ~3> proximity.",
        $"You can restrict your search to the video '{nameof(Video.Title)}', '{nameof(Video.Description)}',"
            + $" '{nameof(Video.Keywords)}' and/or language-specific captions;"
            + $@" e.g. '{nameof(Video.Title)} = ""click bait"" | [English (auto-generated)] = howdy'."];

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
