using CommandLine;

namespace SubTubular;

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