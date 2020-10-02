using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;

namespace SubTubular
{
    internal sealed class Program
    {
        private static async Task Main(string[] args)
        {
            //see https://github.com/commandlineparser/commandline
            var parserResult = Parser.Default.ParseArguments<SearchPlaylist, SearchVideos>(args);

            //https://github.com/commandlineparser/commandline/wiki/Getting-Started#using-withparsedasync-in-asyncawait
            await parserResult.WithParsedAsync<SearchPlaylist>(async command => await Search(
                youtube => youtube.SearchPlaylistAsync(command),
                result => DisplayVideoResult(result, command.Terms)));

            await parserResult.WithParsedAsync<SearchVideos>(async command => await Search(
                youtube => youtube.SearchVideosAsync(command),
                result => DisplayVideoResult(result, command.Terms)));
        }

        private static async Task Search<T>(
            Func<Youtube, IAsyncEnumerable<T>> getResultsAsync,
            Action<T> displayResult)
        {
            var fileStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Assembly.GetEntryAssembly().GetName().Name);

            var youtube = new Youtube(new JsonFileDataStore(fileStoragePath));
            Console.WriteLine();
            var hasResult = false;

            await foreach (var result in getResultsAsync(youtube))
            {
                hasResult = true;
                displayResult(result);
            }

            if (hasResult) Console.WriteLine("All captions were downloaded to " + fileStoragePath);
        }

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

            var matches = terms
                .SelectMany(term => text
                    .IndexesOf(term, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                    .Select(index => new { term.Length, index }))
                .OrderBy(match => match.index)
                .ToArray();

            foreach (var match in matches)
            {
                if (written < match.index) write(match.index - written); //write text preceding match
                if (match.index < written && match.index <= written - match.Length) continue; //letters already matched
                Console.ForegroundColor = highlightedForeGround;
                write(match.Length - (written - match.index)); //write remaining matched letters
                Console.ForegroundColor = regularForeGround;
            }

            if (written < text.Length) write(text.Length - written); //write text trailing last match
        }
        #endregion
    }
}