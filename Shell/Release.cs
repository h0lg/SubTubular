using System.CommandLine;

namespace SubTubular.Shell;

static partial class CommandInterpreter
{
    private static Command ConfigureRelease()
    {
        Command release = new("release", $"List, browse and install other {AssemblyInfo.Name} releases.");
        release.Aliases.Add("rls");

        Command list = new("list", $"Lists available releases from {AssemblyInfo.ReleasesUrl} .");
        list.Aliases.Add("l");
        list.SetAction(async _ => Console.WriteLine(await ReleaseManager.ListAsync(Program.CreateDataStore())));

        Argument<string> version = new("version") { Description = "The version number of a release or 'latest'." };

        Command notes = new("notes", "Opens the github release notes for a single release.");
        notes.Aliases.Add("n");
        notes.Arguments.Add(version);
        notes.SetAction(async parsed => await ReleaseManager.OpenNotesAsync(parsed.Parsed(version), Program.CreateDataStore()));

        Command install = new(ReleaseManager.InstallVersionConsoleCommand, "Downloads a release from github"
            + " and unzips it to the current installation folder while backing up the running version.");

        Option<string> installInto = new(ReleaseManager.InstallFolderConsoleParameter) { Hidden = true };
        installInto.Aliases.Add("-t");

        install.Aliases.Add("i");
        install.Arguments.Add(version);
        install.Options.Add(installInto);

        install.SetAction(async parsed => await ReleaseManager.InstallByTagAsync(
            parsed.Parsed(version), parsed.Parsed(installInto)!, Console.Write, Program.CreateDataStore()));

        release.Subcommands.Add(list);
        release.Subcommands.Add(notes);
        release.Subcommands.Add(install);
        return release;
    }
}