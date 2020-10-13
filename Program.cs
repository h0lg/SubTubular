using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
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

        private static async Task Main(string[] args)
        {
            //see https://github.com/commandlineparser/commandline
            var parserResult = new Parser(with => with.HelpWriter = null)
                .ParseArguments<SearchUser, SearchChannel, SearchPlaylist, SearchVideos, ClearCache>(args);

            var cmd = "> SubTubular " + args.Join(" ");

            //https://github.com/commandlineparser/commandline/wiki/Getting-Started#using-withparsedasync-in-asyncawait
            await parserResult.WithParsedAsync<SearchUser>(async command
                => await Search(command, cmd, youtube => youtube.SearchPlaylistAsync(command)));

            await parserResult.WithParsedAsync<SearchChannel>(async command
                => await Search(command, cmd, youtube => youtube.SearchPlaylistAsync(command)));

            await parserResult.WithParsedAsync<SearchPlaylist>(async command
                => await Search(command, cmd, youtube => youtube.SearchPlaylistAsync(command)));

            await parserResult.WithParsedAsync<SearchVideos>(async command
                => await Search(command, cmd, youtube => youtube.SearchVideosAsync(command)));

            parserResult.WithParsed<ClearCache>(c => new JsonFileDataStore(GetCachePath()).Clear());

            //see https://github.com/commandlineparser/commandline/wiki/HelpText-Configuration
            parserResult.WithNotParsed(errors => Console.WriteLine(HelpText.AutoBuild(parserResult, h =>
            {
                h.Heading = asciiHeading + h.Heading;
                h.AddPreOptionsLine("See https://github.com/h0lg/SubTubular for more info.");
                h.OptionComparison = CompareOptions;
                h.AddPostOptionsLine("Subtitles and metadata are cached in " + GetCachePath());
                h.AddPostOptionsLine(string.Empty);
                return h;
            })));
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
                IDocument document = null;
                IElement output = null;
                var hasOutputPath = !string.IsNullOrEmpty(command.FileOutputPath);
                var writeOutputFile = command.OutputHtml || hasOutputPath;
                StringWriter textOut = null;

                try
                {
                    if (writeOutputFile)
                    {
                        //TODO refactor into output writer
                        if (command.OutputHtml)
                        {
                            var context = BrowsingContext.New(Configuration.Default);
                            document = await context.OpenNewAsync();

                            var style = document.CreateElement("style");
                            style.TextContent = "pre > em { background-color: yellow }";
                            document.Head.Append(style);

                            output = document.CreateElement("pre");
                            document.Body.Append(output);
                        }
                        else
                        {
                            textOut = new StringWriter();
                        }
                    }

                    void write(string text)
                    {
                        Console.Write(text);

                        if (writeOutputFile)
                        {
                            if (command.OutputHtml) output.InnerHtml += text;
                            else textOut.Write(text);
                        }
                    };

                    void writeLine(string text)
                    {
                        Console.WriteLine(text);

                        if (writeOutputFile)
                        {
                            if (command.OutputHtml)
                                output.InnerHtml += (text ?? string.Empty) + Environment.NewLine;
                            else textOut.WriteLine(text);
                        }
                    };

                    var regularForeGround = Console.ForegroundColor; //using current

                    void writeHighlighted(string text)
                    {
                        Console.ForegroundColor = highlightColor;
                        Console.Write(text);
                        Console.ForegroundColor = regularForeGround;

                        if (writeOutputFile)
                        {
                            if (command.OutputHtml)
                            {
                                var em = document.CreateElement("em");
                                em.TextContent = text;
                                output.Append(em);
                            }
                            else textOut.Write($"*{text}*");
                        }
                    };

                    void writeUrl(string url)
                    {
                        Console.Write(url);

                        if (writeOutputFile)
                        {
                            if (command.OutputHtml)
                            {
                                var hlink = document.CreateElement("a");
                                hlink.SetAttribute("href", url);
                                hlink.SetAttribute("target", "_blank");
                                hlink.TextContent = url;
                                output.Append(hlink);
                            }
                            else textOut.Write(url);
                        }
                    };

                    void writeHighlighting(string text) => WriteHighlighting(text, terms, write, writeHighlighted);

                    if (writeOutputFile)
                    {
                        writeLine(originalCommand);
                        writeLine(null);
                    }

                    /* passing token into search implementations for them to react to cancellation,
                        see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables*/
                    await foreach (var result in getResultsAsync(youtube).WithCancellation(search.Token))
                        DisplayVideoResult(result, write, writeLine, writeHighlighting, writeUrl);
                }
                catch (OperationCanceledException) { Console.WriteLine("The search was cancelled."); }

                if (writeOutputFile)
                {
                    string path;

                    if (!hasOutputPath || command.FileOutputPath.IsPathDirectory())
                    {
                        var extension = command.OutputHtml ? ".html" : ".txt";
                        var fileName = command.Format() + extension;
                        var folder = hasOutputPath ? command.FileOutputPath : GetFileStoragePath("out");
                        path = Path.Combine(folder, fileName);
                    }
                    else path = command.FileOutputPath; // treat as full file path

                    var text = command.OutputHtml ? document.DocumentElement.OuterHtml : textOut.ToString();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    await File.WriteAllTextAsync(path, text);
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

        #region DISPLAY
        const ConsoleColor highlightColor = ConsoleColor.Yellow;

        private static void DisplayVideoResult(VideoSearchResult result, Action<string> write, Action<string> writeLine,
            Action<string> writeHighlighted, Action<string> writeUrl)
        {
            var videoUrl = "https://youtu.be/" + result.Video.Id;
            write($"{result.Video.Uploaded:g} ");
            writeUrl(videoUrl);
            writeLine(null);

            if (result.TitleMatches)
            {
                write("  in title: ");
                writeHighlighted(result.Video.Title);
                writeLine(null);
            }

            if (result.MatchingDescriptionLines.Any())
            {
                write("  in description: ");

                for (int i = 0; i < result.MatchingDescriptionLines.Length; i++)
                {
                    var line = result.MatchingDescriptionLines[i];
                    var prefix = i == 0 ? string.Empty : "    ";
                    writeHighlighted(prefix + line);
                    writeLine(null);
                }
            }

            if (result.MatchingKeywords.Any())
            {
                write("  in keywords: ");
                var lastKeyword = result.MatchingKeywords.Last();

                foreach (var keyword in result.MatchingKeywords)
                {
                    writeHighlighted(keyword);

                    if (keyword != lastKeyword) write(", ");
                    else writeLine(null);
                }
            }

            foreach (var track in result.MatchingCaptionTracks)
            {
                writeLine("  " + track.LanguageName);

                var displaysHour = track.Captions.Any(c => c.At > 3600);

                foreach (var caption in track.Captions)
                {
                    var offset = TimeSpan.FromSeconds(caption.At).FormatWithOptionalHours().PadLeft(displaysHour ? 7 : 5);
                    write($"    {offset} ");
                    writeHighlighted(caption.Text);
                    write("    ");
                    writeUrl($"{videoUrl}?t={caption.At}");
                    writeLine(null);
                }
            }

            writeLine(null);
        }

        private static void WriteHighlighting(string text, IEnumerable<string> terms, Action<string> write, Action<string> writeHighlighting)
        {
            var written = 0;

            void writeCounting(int length, bool highlight = false)
            {
                var phrase = text.Substring(written, length);

                if (highlight) writeHighlighting(phrase);
                else write(phrase);

                written += length;
            };

            var matches = text.GetMatches(terms).ToArray();

            foreach (var match in matches)
            {
                if (written < match.Index) writeCounting(match.Index - written); //write text preceding match
                if (match.Index < written && match.Index <= written - match.Length) continue; //letters already matched
                writeCounting(match.Length - (written - match.Index), true); //write remaining matched letters
            }

            if (written < text.Length) writeCounting(text.Length - written); //write text trailing last match
        }
        #endregion

        #region HelpText Option order
        static int CompareOptions(ComparableOption a, ComparableOption b) => ScoreOption(a) < ScoreOption(b) ? -1 : 1;
        static int ScoreOption(ComparableOption option) => (option.IsValue ? -100 : 100) + (option.Required ? -10 : 10) + option.Index;
        #endregion
    }
}