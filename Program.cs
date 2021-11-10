using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace SubTubular
{
    internal sealed class Program
    {
        private const string asciiHeading = @"
   _____       __  ______      __          __
  / ___/__  __/ /_/_  __/_  __/ /_  __  __/ /___ ______
  \__ \/ / / / __ \/ / / / / / __ \/ / / / / __ `/ ___/
 ___/ / /_/ / /_/ / / / /_/ / /_/ / /_/ / / /_/ / /
/____/\__,_/_.___/_/  \__,_/_.___/\__,_/_/\__,_/_/

"; //from http://www.patorjk.com/software/taag/#p=display&f=Slant&t=SubTubular

        private const string repoUrl = "https://github.com/h0lg/SubTubular";

        private static async Task Main(string[] args)
        {
            var originalCommand = "> SubTubular " + args.Join(" ");

            //see https://github.com/commandlineparser/commandline
            try
            {
                var parserResult = new Parser(with => with.HelpWriter = null)
                    .ParseArguments<SearchUser, SearchChannel, SearchPlaylist, SearchVideos, ClearCache>(args);

                //https://github.com/commandlineparser/commandline/wiki/Getting-Started#using-withparsedasync-in-asyncawait
                await parserResult.WithParsedAsync<SearchUser>(async command
                    => await Search(command, originalCommand, youtube => youtube.SearchPlaylistAsync(command)));

                await parserResult.WithParsedAsync<SearchChannel>(async command
                    => await Search(command, originalCommand, youtube => youtube.SearchPlaylistAsync(command)));

                await parserResult.WithParsedAsync<SearchPlaylist>(async command
                    => await Search(command, originalCommand, youtube => youtube.SearchPlaylistAsync(command)));

                await parserResult.WithParsedAsync<SearchVideos>(async command
                    => await Search(command, originalCommand, youtube => youtube.SearchVideosAsync(command)));

                parserResult.WithParsed<ClearCache>(c => new JsonFileDataStore(GetCachePath()).Clear());

                //see https://github.com/commandlineparser/commandline/wiki/HelpText-Configuration
                parserResult.WithNotParsed(errors => Console.WriteLine(HelpText.AutoBuild(parserResult, h =>
                {
                    h.Heading = asciiHeading + h.Heading;
                    h.AddPreOptionsLine($"See {repoUrl} for more info.");
                    h.OptionComparison = CompareOptions;
                    h.AddPostOptionsLine("Subtitles and metadata are cached in " + GetCachePath());
                    h.AddPostOptionsLine(string.Empty);
                    return h;
                })));
            }
            catch (Exception ex)
            {
                try
                {
                    var errorFolder = GetFileStoragePath("errors");
                    var output = new OutputWriter(originalCommand, false, errorFolder, $"error {DateTime.Now:yyyy-MM-dd HHmmss}");
                    output.WriteLine(ex.ToString());
                    var path = await output.WriteOutputFile(() => errorFolder);
                    Console.WriteLine("Error was logged to " + path);
                }
                catch
                {
                    Console.Error.WriteLine("The following error occurred and we were unabled to write a log for it.");
                    Console.Error.WriteLine(originalCommand);
                    Console.Error.WriteLine(ex.ToString());
                }

                Console.WriteLine();
                Console.WriteLine($"Check out {repoUrl}/issues for existing reports of this error and maybe a solution or work-around."
                    + " If you want to report it, you can do that there to. Please make sure to supply the error details. Thanks!");
            }
        }

        private static async Task Search(SearchCommand command, string originalCommand,
            Func<Youtube, IAsyncEnumerable<VideoSearchResult>> getResultsAsync)
        {
            var terms = command.Terms;

            if (!terms.Any())
            {
                Console.WriteLine("None of the terms contain anything but whitespace. I refuse to work like this!");
                return;
            }

            //inspired by https://johnthiriet.com/cancel-asynchronous-operation-in-csharp/
            using (var search = new CancellationTokenSource())
            {
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

                var youtube = new Youtube(new JsonFileDataStore(GetCachePath()));

                using (var output = new OutputWriter(originalCommand, command))
                {
                    try
                    {
                        /* passing token into search implementations for them to react to cancellation,
                            see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables*/
                        await foreach (var result in getResultsAsync(youtube).WithCancellation(search.Token))
                            output.DisplayVideoResult(result);
                    }
                    catch (OperationCanceledException) { Console.WriteLine("The search was cancelled."); }

                    var path = await output.WriteOutputFile(() => GetFileStoragePath("out"));
                    Console.WriteLine("Search results were written to " + path);
                }

                searching = false; //to complete the cancel task
                await cancel; //just to rethrow possible exceptions
            }
        }

        private static string GetFileStoragePath(string subFolder) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Assembly.GetEntryAssembly().GetName().Name, subFolder);

        private static string GetCachePath() => GetFileStoragePath("cache");

        #region HelpText Option order
        static int CompareOptions(ComparableOption a, ComparableOption b) => ScoreOption(a) < ScoreOption(b) ? -1 : 1;
        static int ScoreOption(ComparableOption option) => (option.IsValue ? -100 : 100) + (option.Required ? -10 : 10) + option.Index;
        #endregion
    }
}