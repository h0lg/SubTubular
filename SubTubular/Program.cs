using System.Runtime.InteropServices;
using CommandLine;
using CommandLine.Text;
using Lifti;

namespace SubTubular;

internal static class Program
{
    private const string asciiHeading = @"
   _____       __  ______      __          __
  / ___/__  __/ /_/_  __/_  __/ /_  __  __/ /___ ______
  \__ \/ / / / __ \/ / / / / / __ \/ / / / / __ `/ ___/
 ___/ / /_/ / /_/ / / / /_/ / /_/ / /_/ / / /_/ / /
/____/\__,_/_.___/_/  \__,_/_.___/\__,_/_/\__,_/_/

"; //from http://www.patorjk.com/software/taag/#p=display&f=Slant&t=SubTubular

    internal const string Name = nameof(SubTubular),
        RepoOwner = "h0lg", RepoName = Name, RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}",
        IssuesUrl = $"{RepoUrl}/issues", ReleasesUrl = $"{RepoUrl}/releases";

    internal static string OutputSpacing = Environment.NewLine + Environment.NewLine;

    private static async Task Main(string[] args)
    {
        var originalCommand = $"> {Name}.exe "
            // quote shell args including pipes to accurately represent the console command
            + args.Select(arg => arg.Contains('|') ? $"\"{arg.Replace("\"", "\"\"")}\"" : arg).Join(" ");

        //see https://github.com/commandlineparser/commandline
        try
        {
            var parserResult = new Parser(with => with.HelpWriter = null)
                .ParseArguments<SearchChannel, SearchPlaylist, SearchVideos, Open, ClearCache, Release>(args);

            //https://github.com/commandlineparser/commandline/wiki/Getting-Started#using-withparsedasync-in-asyncawait
            await parserResult.WithParsedAsync<SearchChannel>(command
                => SearchAsync(command, originalCommand, youtube => youtube.SearchPlaylistAsync(command)));

            await parserResult.WithParsedAsync<SearchPlaylist>(command
                => SearchAsync(command, originalCommand, youtube => youtube.SearchPlaylistAsync(command)));

            await parserResult.WithParsedAsync<SearchVideos>(command
                => SearchAsync(command, originalCommand, youtube => youtube.SearchVideosAsync(command)));

            await parserResult.WithParsedAsync<ClearCache>(async command =>
            {
                (IEnumerable<string> cachesDeleted, IEnumerable<string> indexesDeleted) = await command.Process();

                if (command.Mode != ClearCache.Modes.summary)
                {
                    Console.WriteLine(cachesDeleted.Join(" "));
                    Console.WriteLine();
                    Console.WriteLine(indexesDeleted.Join(" "));
                    Console.WriteLine();
                }

                var be = command.Mode == ClearCache.Modes.simulate ? "would be" : "were";
                Console.WriteLine($"{cachesDeleted.Count()} info caches and {indexesDeleted.Count()} full-text indexes {be} deleted.");
            });

            await parserResult.WithParsedAsync<Release>(async release =>
            {
                var dataStore = new JsonFileDataStore(Folder.GetPath(Folders.cache));

                if (release.List) Console.WriteLine(await ReleaseManager.ListAsync(dataStore));
                else if (!string.IsNullOrEmpty(release.Notes)) await ReleaseManager.OpenNotesAsync(release.Notes, dataStore);
                else if (!string.IsNullOrEmpty(release.InstallVersion)) await ReleaseManager.InstallByTagAsync(release, Console.Write, dataStore);
            });

            parserResult.WithParsed<Open>(open => ShellCommands.ExploreFolder(Folder.GetPath(open.Folder)));

            //see https://github.com/commandlineparser/commandline/wiki/HelpText-Configuration
            parserResult.WithNotParsed(errors => Console.WriteLine(HelpText.AutoBuild(parserResult, h =>
            {
                if (parserResult.Errors.Any(error => error.Tag == ErrorType.NoVerbSelectedError))
                    h.Heading = asciiHeading + h.Heading; // enhance heading for branding
                else // remove heading and copyright to reduce noise before error if a verb is already selected
                {
                    h.Heading = string.Empty;
                    h.Copyright = string.Empty;
                }

                h.MaximumDisplayWidth = Console.WindowWidth;
                h.AddEnumValuesToHelpText = true;
                h.OptionComparison = CompareOptions;
                h.AddPostOptionsLine($"See {RepoUrl} for more info.");
                return h;
            })));
        }
        catch (Exception ex)
        {
            var cause = ex.GetBaseException();

            if (cause is InputException || cause is LiftiException)
            {
                Console.Error.WriteLine(cause.Message);
                return;
            }

            if (cause is HttpRequestException)
                Console.Error.WriteLine("An unexpected error occurred loading data from YouTube."
                    + " Try again later or with an updated version. " + cause.Message);

            await WriteErrorLogAsync(originalCommand, ex.ToString());
        }
    }

    private static async Task SearchAsync(SearchCommand command, string originalCommand,
        Func<Youtube, IAsyncEnumerable<VideoSearchResult>> getResultsAsync)
    {
        command.Validate();

        //inspired by https://johnthiriet.com/cancel-asynchronous-operation-in-csharp/
        using var search = new CancellationTokenSource();
        var searching = true;

        var cancel = Task.Run(async () => //start in background, don't wait for completion
        {
            Console.WriteLine("Press any key to cancel");
            Console.WriteLine();

            /* wait for key or search to finish, non-blockingly; but only as long as to not cause perceivable lag
                inspired by https://stackoverflow.com/a/5620647 and https://stackoverflow.com/a/23628232 */
            while (searching && !Console.KeyAvailable) await Task.Delay(200, search.Token);

            if (Console.KeyAvailable) Console.ReadKey(true); //consume cancel key without displaying it
            if (searching) search.Cancel();
        });

        var cacheFolder = Folder.GetPath(Folders.cache);
        var youtube = new Youtube(new JsonFileDataStore(cacheFolder), new VideoIndexRepository(cacheFolder));
        if (command is RemoteValidated remoteValidated) await youtube.RemoteValidateAsync(remoteValidated, search.Token);
        var tracksWithErrors = new List<CaptionTrack>();

        using (var output = new OutputWriter(command))
        {
            output.WriteHeader(originalCommand);
            var resultDisplayed = false;

            try
            {
                if (command.ListKeywords)
                {
                    var keywords = await youtube.ListKeywordsAsync(command, search.Token);

                    if (keywords.Any())
                    {
                        output.ListKeywords(keywords);
                        resultDisplayed = true;
                    }
                    else Console.WriteLine("Found no keywords.");
                }
                else // search videos in scope
                {
                    /*  passing token into search implementations for them to react to cancellation,
                        see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables */
                    await foreach (var result in getResultsAsync(youtube).WithCancellation(search.Token))
                    {
                        output.DisplayVideoResult(result);
                        resultDisplayed = true;
                        tracksWithErrors.AddRange(result.Video.CaptionTracks.Where(t => t.Error != null));
                    }
                }
            }
            catch (OperationCanceledException) { Console.WriteLine("The search was cancelled."); }
            finally // write output file even if exception occurs
            {
                if (resultDisplayed) // if we displayed a result before running into an error
                {
                    // only writes an output file if command requires it
                    var path = await output.WriteOutputFile(() => Folder.GetPath(Folders.output));

                    if (path != null)
                    {
                        Console.WriteLine("Search results were written to " + path);

                        // spare the user some file browsing
                        if (command.Show == SearchCommand.Shows.file) ShellCommands.OpenFile(path);
                        if (command.Show == SearchCommand.Shows.folder) ShellCommands.ExploreFolder(path);
                    }
                }
            }
        }

        if (tracksWithErrors.Count > 0)
        {
            await WriteErrorLogAsync(originalCommand, tracksWithErrors.Select(t =>
@$"{t.LanguageName}: {t.ErrorMessage}

  {t.Url}

  {t.Error}").Join(OutputSpacing), command.Describe());
        }

        searching = false; // to let the cancel task complete if search did before it
        await cancel; // just to rethrow possible exceptions
    }

    private static async Task WriteErrorLogAsync(string originalCommand, string errors, string name = null)
    {
        var productInfo = Name + " " + AssemblyInfo.GetProductVersion();

        var environmentInfo = new[] { "on", Environment.OSVersion.VersionString,
            RuntimeInformation.FrameworkDescription, productInfo }.Join(" ");

        var report = (new[] { originalCommand, environmentInfo, errors }).Join(OutputSpacing);
        var fileWritten = false;

        try
        {
            var fileSafeName = name == null ? null : (" " + name.ToFileSafe());
            var path = Path.Combine(Folder.GetPath(Folders.errors), $"error {DateTime.Now:yyyy-MM-dd HHmmss}{fileSafeName}.txt");
            await OutputWriter.WriteTextToFileAsync(report, path);
            Console.WriteLine("Errors were logged to " + path);
            fileWritten = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("The following errors occurred and we were unable to write a log for them.");
            Console.WriteLine();
            Console.Error.WriteLine(report);
            Console.WriteLine();
            Console.Error.WriteLine("Error writing error log: " + ex);
        }

        Console.WriteLine();

        Console.WriteLine($"Try 'release --list' or check {ReleasesUrl} for a version newer than {productInfo} that may have fixed this"
            + $" or {IssuesUrl} for existing reports of this error and maybe a solution or work-around."
            + " If you can reproduce this error in the latest version, reporting it there is your best chance at getting it fixed."
            + " If you do, make sure to include the original command or parameters to reproduce it,"
            + $" any exception details that have not already been shared and the OS/.NET/{Name} version you're on."
            + $" You'll find all that in the error {(fileWritten ? "log file" : "output above")}.");
    }

    #region HelpText Option order
    private static int CompareOptions(ComparableOption a, ComparableOption b)
        => ScoreOption(a) < ScoreOption(b) ? -1 : 1;

    private static int ScoreOption(ComparableOption option)
        => (option.IsValue ? -100 : 100) + (option.Required ? -10 : 10) + option.Index;
    #endregion
}