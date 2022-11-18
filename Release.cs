using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Octokit;

namespace SubTubular
{
    [Verb("release", aliases: new[] { "r" }, HelpText = $"List, browse and install other {Program.Name} releases.")]
    internal sealed class Release
    {
        private const string actions = "actions", into = "into", install = "install";

        [Option('l', "list", Group = actions,
            HelpText = $"Lists available releases from {Program.ReleasesUrl} .")]
        public bool List { get; set; }

        [Option('n', "notes", Group = actions, HelpText = "Opens the github release notes for a single release."
            + " Supply either the tag of the release you're interested in or 'latest'.")]
        public string Notes { get; set; }

        [Option('t', into, Hidden = true)]
        public string InstallFolder { get; set; }

        [Option('i', install, Group = actions, HelpText = "Downloads a release from github"
            + " and unzips it to the current installation folder while backing up the running version."
            + " Supply either the tag of the release to install or 'latest'.")]
        public string InstallTag { get; set; }

        internal async Task<string> ListAsync()
        {
            var releases = await GetAll();
            var maxTagLength = releases.Max(r => r.TagName.Length);

            return releases.Select(r => $"{r.PublishedAt:yyyy-MM-dd} {r.TagName.PadLeft(maxTagLength)} {r.Name}"
                + (r.TagName.Contains(AssemblyInfo.Version) ? "     <<<<<  YOU ARE HERE" : null))
                .Prepend("date       " + "tag".PadRight(maxTagLength) + " name").Join(Environment.NewLine);
        }

        internal async Task InstallByTagAsync(Action<string> report)
        {
            var release = await GetRelease(InstallTag);

            if (string.IsNullOrEmpty(InstallFolder)) // STEP 1, running in app to be replaced
            {
                // back up current app
                var appFolder = Path.GetDirectoryName(AssemblyInfo.Location);
                var archiveFolder = GetArchivePath(appFolder);
                var productInfo = Program.Name + " " + AssemblyInfo.Version;
                var backupFolder = Path.Combine(archiveFolder, productInfo.ToFileSafe());

                report($"Backing up installed {productInfo} binaries{Environment.NewLine}to '{backupFolder}' ... ");

                foreach (var filePath in FileHelper.GetFilesExcluding(appFolder, archiveFolder))
                {
                    var targetFilePath = filePath.Replace(appFolder, backupFolder);
                    FileHelper.CreateFolder(targetFilePath);
                    File.Copy(filePath, targetFilePath);
                }

                report("DONE" + Environment.NewLine);

                /*  start STEP 2 on backed up binaries and have them replace
                    the ones in the current location with the requested version */
                Process.Start(Path.Combine(backupFolder, Program.Name + ".exe"),
                    $"release --{install} {release.TagName} --{into} {appFolder}");
            }
            else // STEP 2, running on backed up binaries of app to be replaced (in archive sub folder)
            {
                var zips = release.Assets.Where(asset => asset.Name.EndsWith(".zip")).ToArray();

                if (zips.Length > 1) throw new NotImplementedException(
                    $"Release with tag {release.TagName} contains mulitple custom .zip assets (other than the source code)."
                    + " To support automatic release installation,"
                    + " implement a strategy to select the one with the (correct) binaries to download.");

                var asset = zips.SingleOrDefault();

                if (asset == null) throw new Exception(
                    $"Release with tag {release.TagName} doesn't contain a .zip asset with binaries that could be downloaded.");

                var archiveFolder = GetArchivePath(InstallFolder);
                var zipPath = Path.Combine(archiveFolder, asset.Name);
                var zipFile = new FileInfo(zipPath);
                report(Environment.NewLine); // to start in a new line below the prompt

                if (!zipFile.Exists || asset.Size != zipFile.Length)
                {
                    var url = asset.BrowserDownloadUrl;
                    report($"Downloading {release.TagName} from '{url}'{Environment.NewLine}to '{zipPath}' ... ");
                    await FileHelper.DownloadAsync(url, zipPath);
                    report("DONE" + Program.OutputSpacing);
                }

                report($"Removing installed binaries from '{InstallFolder}' ... ");
                foreach (var filePath in FileHelper.GetFilesExcluding(InstallFolder, archiveFolder)) File.Delete(filePath);
                report("DONE" + Program.OutputSpacing);

                report($"Unpacking '{zipPath}'{Environment.NewLine}into '{InstallFolder}' ... ");
                FileHelper.Unzip(zipPath, InstallFolder);
                report("DONE" + Program.OutputSpacing);

                report($"The binaries in '{InstallFolder}'" + Environment.NewLine
                    + $"have successfully been updated to {release.TagName} - opening release notes.");

                OpenNotes(release); // by default to update users about changes
            }
        }

        internal static async Task OpenNotesAsync(string tag) => OpenNotes(await GetRelease(tag));
        private static void OpenNotes(Octokit.Release release) => ShellCommands.OpenUri(release.HtmlUrl);
        private static string GetArchivePath(string appFolder) => Path.Combine(appFolder, "other releases");

        private static GitHubClient GetGithubClient()
            => new GitHubClient(new ProductHeaderValue(Program.Name, AssemblyInfo.GetProductVersion()));

        private static async Task<IReadOnlyList<Octokit.Release>> GetAll()
            => await GetGithubClient().Repository.Release.GetAll(Program.RepoOwner, Program.RepoName);

        private static async Task<Octokit.Release> GetRelease(string tag)
        {
            if (tag == "latest") return (await GetAll()).OrderBy(r => r.PublishedAt).Last();

            try { return await GetGithubClient().Repository.Release.Get(Program.RepoOwner, Program.RepoName, tag); }
            catch (NotFoundException ex)
            {
                throw new InputException($"Release with tag {tag} could not be found."
                    + " Use a value from the 'tag' column in the 'release --list'.", ex);
            }
        }
    }
}