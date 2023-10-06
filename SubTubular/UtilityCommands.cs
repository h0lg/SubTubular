using CommandLine;

namespace SubTubular;

[Verb(Command, aliases: new[] { "clear" },
    HelpText = "Deletes cached metadata and full-text indexes for "
        + $"{nameof(Scopes.channels)}, {nameof(Scopes.playlists)} and {nameof(Scopes.videos)}.")]
internal sealed class ClearCache
{
    internal const string Command = "clear-cache";
    private const string scope = "scope", ids = "ids";

    [Value(0, MetaName = scope, Required = true, HelpText = "The type of caches to delete."
        + $" For {nameof(Scopes.playlists)} and {nameof(Scopes.channels)}"
        + $" this will include the associated {nameof(Scopes.videos)}.")]
    public Scopes Scope { get; set; }

    [Value(1, MetaName = ids, HelpText = $"The space-separated IDs or URLs of elements in the '{scope}' to delete caches for."
        + $" Can be used with every '{scope}' but '{nameof(Scopes.all)}'"
        + $" while supporting user names, channel handles and slugs besides IDs for '{nameof(Scopes.channels)}'."
        + $" If not set, all elements in the specified '{scope}' are considered for deletion."
        + SearchVideos.QuoteIdsStartingWithDash)]
    public IEnumerable<string> Ids { get; set; }

    [Option('l', "last-access",
        HelpText = "The maximum number of days since the last access of a cache file for it to be excluded from deletion."
            + " Effectively only deletes old caches that haven't been accessed for this number of days."
            + $" Ignored for explicitly set '{ids}'.")]
    public ushort? NotAccessedForDays { get; set; }

    [Option('m', "mode", Default = Modes.summary,
        HelpText = "The deletion mode;"
            + $" '{nameof(Modes.summary)}' only outputs how many of what file type were deleted."
            + $" '{nameof(Modes.verbose)}' outputs the deleted file names as well as the summary."
            + $" '{nameof(Modes.simulate)}' lists all file names that would be deleted by running the command instead of deleting them."
            + " You can use this to preview the files that would be deleted.")]
    public Modes Mode { get; set; }

    internal enum Scopes { all, videos, playlists, channels }
    internal enum Modes { summary, verbose, simulate }
}

[Verb("release", aliases: new[] { "r" }, HelpText = $"List, browse and install other {Program.Name} releases.")]
internal sealed class Release
{
    internal const string InstallVersionConsoleParameter = "install",
        InstallFolderConsoleParameter = "into";

    private const string actions = "actions";

    [Option('l', "list", Group = actions,
        HelpText = $"Lists available releases from {Program.ReleasesUrl} .")]
    public bool List { get; set; }

    [Option('n', "notes", Group = actions, HelpText = "Opens the github release notes for a single release."
        + " Supply either the version of the release you're interested in or 'latest'.")]
    public string Notes { get; set; }

    [Option('t', InstallFolderConsoleParameter, Hidden = true)]
    public string InstallFolder { get; set; }

    [Option('i', InstallVersionConsoleParameter, Group = actions, HelpText = "Downloads a release from github"
        + " and unzips it to the current installation folder while backing up the running version."
        + " Supply either the version of the release to install or 'latest'.")]
    public string InstallVersion { get; set; }
}

[Verb("open", aliases: new[] { "o" }, HelpText = "Opens app-related folders in a file browser.")]
internal sealed class Open
{
    [Value(0, MetaName = "folder", Required = true, HelpText = "The folder to open.")]
    public Folders Folder { get; set; }
}