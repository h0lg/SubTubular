using System.Diagnostics;
using Octokit;

namespace SubTubular;

internal static class ReleaseManager
{
    internal static async Task<string> ListAsync(DataStore dataStore)
    {
        var releases = await GetAll(dataStore);
        const string version = "version";
        var maxVersionLength = Math.Max(version.Length, releases.Max(r => r.Version.Length));

        return releases.Select(r => $"{r.PublishedAt:yyyy-MM-dd} {r.Version.PadLeft(maxVersionLength)} {r.Name}"
            + (r.Version == AssemblyInfo.Version ? "     <<<<<  YOU ARE HERE" : null))
            .Prepend("date       " + version.PadRight(maxVersionLength) + " name").Join(Environment.NewLine);
    }

    internal static async Task InstallByTagAsync(Release command, Action<string> report, DataStore dataStore)
    {
        var release = await GetRelease(command.InstallVersion, dataStore);

        if (release.Version == AssemblyInfo.Version)
            throw new InputException($"Release {release.Version} is already installed.");

        if (release.BinariesZipError != null) throw new InputException(
            $"Installing release {release.Version} is not supported because it contains {release.BinariesZipError}.");

        if (string.IsNullOrEmpty(command.InstallFolder)) // STEP 1, running in app to be replaced
        {
            // back up current app
            var appFolder = Path.GetDirectoryName(AssemblyInfo.Location);
            var archiveFolder = GetArchivePath(appFolder);
            var productInfo = AssemblyInfo.Name + " " + AssemblyInfo.Version;
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
            Process.Start(Path.Combine(backupFolder, AssemblyInfo.Name + ".exe"),
                $"release --{Release.InstallVersionConsoleParameter} {release.Version} --{Release.InstallFolderConsoleParameter} {appFolder}");
        }
        else // STEP 2, running on backed up binaries of app to be replaced (in archive sub folder)
        {
            var archiveFolder = GetArchivePath(command.InstallFolder);
            var zipPath = Path.Combine(archiveFolder, release.BinariesZip.Name);
            var zipFile = new FileInfo(zipPath);
            report(Environment.NewLine); // to start in a new line below the prompt

            // compare size of downloaded zip with online asset as a simple form of validation
            if (!zipFile.Exists || release.BinariesZip.Size != zipFile.Length)
            {
                var url = release.BinariesZip.DownloadUrl;
                report($"Downloading {release.Version} from '{url}'{Environment.NewLine}to '{zipPath}' ... ");
                await FileHelper.DownloadAsync(url, zipPath);
                report("DONE" + AssemblyInfo.OutputSpacing);
            }

            report($"Removing installed binaries from '{command.InstallFolder}' ... ");
            foreach (var filePath in FileHelper.GetFilesExcluding(command.InstallFolder, archiveFolder)) File.Delete(filePath);
            report("DONE" + AssemblyInfo.OutputSpacing);

            report($"Unpacking '{zipPath}'{Environment.NewLine}into '{command.InstallFolder}' ... ");
            FileHelper.Unzip(zipPath, command.InstallFolder);
            report("DONE" + AssemblyInfo.OutputSpacing);

            report($"The binaries in '{command.InstallFolder}'" + Environment.NewLine
                + $"have successfully been updated to {release.Version} - opening release notes.");

            OpenNotes(release); // by default to update users about changes
        }
    }

    internal static async Task OpenNotesAsync(string version, DataStore dataStore)
        => OpenNotes(await GetRelease(version, dataStore));

    private static void OpenNotes(CacheModel release) => ShellCommands.OpenUri(release.HtmlUrl);
    private static string GetArchivePath(string appFolder) => Path.Combine(appFolder, "other releases");

    private static GitHubClient GetGithubClient()
        => new GitHubClient(new ProductHeaderValue(AssemblyInfo.Name, AssemblyInfo.GetProductVersion()));

    private static async Task<List<CacheModel>> GetAll(DataStore dataStore)
    {
        const string cacheKey = "releases";
        var lastModified = dataStore.GetLastModified(cacheKey);

        // if cache exists and is not older than 1 hour
        if (lastModified.HasValue && DateTime.Now.Subtract(lastModified.Value).TotalHours < 1)
        {
            var cached = await dataStore.GetAsync<List<CacheModel>>(cacheKey);
            if (cached != null) return cached;
        }

        var freshReleases = await GetGithubClient().Repository.Release.GetAll(AssemblyInfo.RepoOwner, AssemblyInfo.RepoName);
        var releases = freshReleases.Select(release => new CacheModel(release)).ToList();
        await dataStore.SetAsync(cacheKey, releases);
        return releases;
    }

    private static async Task<CacheModel> GetRelease(string version, DataStore dataStore)
    {
        var releases = await GetAll(dataStore);
        if (version == "latest") return releases.OrderBy(r => r.PublishedAt).Last();
        var containing = releases.Where(r => r.Version.Contains(version)).ToArray();

        if (containing.Length < 1) throw new InputException($"Release with version {version} could not be found."
            + " Use a value from the 'version' column in the 'release --list'.");

        if (containing.Length == 1) return containing.Single();

        var matching = containing.Where(r => r.Version == version).ToArray();
        if (matching.Length == 1) return matching[0];

        throw new InputException($"'{version}' matches multiple release versions."
            + " Specify a unique value: " + containing.Select(r => r.Version).Join(" | "));
    }

    [Serializable]
    public sealed class CacheModel
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string HtmlUrl { get; set; }
        public DateTime PublishedAt { get; set; }

        /// <summary>The .zip asset containing the binaries if <see cref="BinariesZipError"/> is null.</summary>
        public BinariesZipAsset BinariesZip { get; set; }

        /// <summary>The error identifying the <see cref="BinariesZip"/>, if any.</summary>
        public string BinariesZipError { get; set; }

        public CacheModel() { } // required for serialization

        internal CacheModel(Octokit.Release release)
        {
            Name = release.Name;
            Version = release.TagName.TrimStart('v');
            HtmlUrl = release.HtmlUrl;
            PublishedAt = release.PublishedAt.GetValueOrDefault().DateTime;

            var zips = release.Assets.Where(asset => asset.Name.EndsWith(".zip")).ToArray();

            if (zips.Length > 1)
            {
                /*  To support automatic release installation with multiple matching zip files,
                    implement a strategy to select the one with the (correct) binaries to download. */
                BinariesZipError = "multiple .zip assets"; // custom, other than the source code
                return;
            }

            var asset = zips.SingleOrDefault();

            if (asset == null) BinariesZipError = "no .zip asset";
            else BinariesZip = new BinariesZipAsset
            {
                Name = asset.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                Size = asset.Size
            };
        }

        [Serializable]
        public sealed class BinariesZipAsset
        {
            public string Name { get; set; }
            public string DownloadUrl { get; set; }
            public int Size { get; set; }
        }
    }
}