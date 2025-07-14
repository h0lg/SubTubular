using System.CommandLine;

namespace SubTubular.Shell;

static partial class CommandInterpreter
{
    private static Command ConfigureRelease()
    {
        Command release = new("release", $"List, browse and install other {AssemblyInfo.Name} releases.");
        release.AddAlias("rls");

        Command list = new("list", $"Lists available releases from {AssemblyInfo.ReleasesUrl} .");
        list.AddAlias("l");
        list.SetHandler(async () => Console.WriteLine(await ReleaseManager.ListAsync(Program.CreateDataStore())));

        Argument<string> version = new("version", "The version number of a release or 'latest'.");

        Command notes = new("notes", "Opens the github release notes for a single release.");
        notes.AddAlias("n");
        notes.AddArgument(version);
        notes.SetHandler(async (version) => await ReleaseManager.OpenNotesAsync(version, Program.CreateDataStore()), version);

        Command install = new(ReleaseManager.InstallVersionConsoleCommand, "Downloads a release from github"
            + " and unzips it to the current installation folder while backing up the running version.");

        Option<string> installInto = new(ReleaseManager.InstallFolderConsoleParameter) { IsHidden = true };
        installInto.AddAlias("-t");

        install.AddAlias("i");
        install.AddArgument(version);
        install.AddOption(installInto);

        install.SetHandler(async (version, installInto) => await ReleaseManager.InstallByTagAsync(
            version, installInto, Console.Write, Program.CreateDataStore()), version, installInto);

        release.AddCommand(list);
        release.AddCommand(notes);
        release.AddCommand(install);
        return release;
    }
}