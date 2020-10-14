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
        private static async Task Main(string[] args)
        {
            //see https://github.com/commandlineparser/commandline
            var parserResult = new Parser(with => with.HelpWriter = null)
                .ParseArguments<SearchUser, SearchChannel, SearchPlaylist, SearchVideos, ClearCache>(args);

            //https://github.com/commandlineparser/commandline/wiki/Getting-Started#using-withparsedasync-in-asyncawait
            await parserResult.WithParsedAsync<SearchUser>(async command => await Search(command,
                youtube => youtube.SearchPlaylistAsync(command),
                result => DisplayVideoResult(result, command.Terms)));

            await parserResult.WithParsedAsync<SearchChannel>(async command => await Search(command,
                youtube => youtube.SearchPlaylistAsync(command),
                result => DisplayVideoResult(result, command.Terms)));

            await parserResult.WithParsedAsync<SearchPlaylist>(async command => await Search(command,
                youtube => youtube.SearchPlaylistAsync(command),
                result => DisplayVideoResult(result, command.Terms)));

            await parserResult.WithParsedAsync<SearchVideos>(async command => await Search(command,
                youtube => youtube.SearchVideosAsync(command),
                result => DisplayVideoResult(result, command.Terms)));

            parserResult.WithParsed<ClearCache>(c => new JsonFileDataStore(GetFileStoragePath()).Clear());

            //see https://github.com/commandlineparser/commandline/wiki/HelpText-Configuration
            parserResult.WithNotParsed(errors => Console.WriteLine(HelpText.AutoBuild(parserResult, h =>
            {
                h.AddPreOptionsLine("See https://github.com/h0lg/SubTubular for more info.");
                h.OptionComparison = CompareOptions;
                h.AddPostOptionsLine("Subtitles and metadata are cached in " + GetFileStoragePath());
                h.AddPostOptionsLine(string.Empty);
                return h;
            })));
        }

        private static async Task Search<T>(
            SearchCommand command,
            Func<Youtube, IAsyncEnumerable<T>> getResultsAsync,
            Action<T> displayResult)
        {
            if (!command.Terms.Any())
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

                var youtube = new Youtube(new JsonFileDataStore(GetFileStoragePath()));

                try
                {
                    /* passing token into search implementations for them to react to cancellation,
                        see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables*/
                    await foreach (var result in getResultsAsync(youtube).WithCancellation(search.Token))
                    {
                        displayResult(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("The search was cancelled.");
                }

                searching = false; //to complete the cancel task
                await cancel; //just to rethrow possible exceptions
            }
        }

        private static string GetFileStoragePath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Assembly.GetEntryAssembly().GetName().Name);

        #region DISPLAY
        const ConsoleColor highlightColor = ConsoleColor.Yellow;

        private static void DisplayVideoResult(VideoSearchResult result, IEnumerable<string> terms)
        {
            var videoUrl = "https://youtu.be/" + result.Video.Id;
            Console.WriteLine($"{result.Video.Uploaded:g} {videoUrl}");

            if (result.TitleMatches)
            {
                WriteHighlighting("  in title: " + result.Video.Title, terms, highlightColor);
                Console.WriteLine();
            }

            if (result.MatchingDescriptionLines.Any())
            {
                Console.Write("  in description: ");

                for (int i = 0; i < result.MatchingDescriptionLines.Length; i++)
                {
                    var line = result.MatchingDescriptionLines[i];
                    var prefix = i == 0 ? string.Empty : "    ";
                    WriteHighlighting(prefix + line, terms, highlightColor);
                    Console.WriteLine();
                }
            }

            if (result.MatchingKeywords.Any())
            {
                Console.Write("  in keywords: ");
                var lastKeyword = result.MatchingKeywords.Last();

                foreach (var keyword in result.MatchingKeywords)
                {
                    WriteHighlighting(keyword, terms, highlightColor);

                    if (keyword != lastKeyword) Console.Write(", ");
                    else Console.WriteLine();
                }
            }

            foreach (var track in result.MatchingCaptionTracks)
            {
                Console.WriteLine("  " + track.LanguageName);

                var displaysHour = track.Captions.Any(c => c.At > 3600);

                foreach (var caption in track.Captions)
                {
                    var offset = TimeSpan.FromSeconds(caption.At).FormatWithOptionalHours().PadLeft(displaysHour ? 7 : 5);
                    Console.Write($"    {offset} ");
                    WriteHighlighting(caption.Text, terms, highlightColor);
                    Console.WriteLine($"    {videoUrl}?t={caption.At}");
                }
            }

            Console.WriteLine();
        }

        private static void WriteHighlighting(string text, IEnumerable<string> terms, ConsoleColor highlightedForeGround)
        {
            var written = 0;
            var regularForeGround = Console.ForegroundColor; //using current

            Action<int> write = length =>
            {
                Console.Write(text.Substring(written, length));
                written += length;
            };

            var matches = text.GetMatches(terms).ToArray();

            foreach (var match in matches)
            {
                if (written < match.Index) write(match.Index - written); //write text preceding match
                if (match.Index < written && match.Index <= written - match.Length) continue; //letters already matched
                Console.ForegroundColor = highlightedForeGround;
                write(match.Length - (written - match.Index)); //write remaining matched letters
                Console.ForegroundColor = regularForeGround;
            }

            if (written < text.Length) write(text.Length - written); //write text trailing last match
        }
        #endregion

        #region HelpText Option order
        static int CompareOptions(ComparableOption a, ComparableOption b) => ScoreOption(a) < ScoreOption(b) ? -1 : 1;
        static int ScoreOption(ComparableOption option) => (option.IsValue ? -100 : 100) + (option.Required ? -10 : 10) + option.Index;
        #endregion
    }
}