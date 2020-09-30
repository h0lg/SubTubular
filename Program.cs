using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
                result => DisplayVideoResult(result.Item1.Id, result.Item2, result.Item1.Uploaded.ToString("g") + " ")));

            await parserResult.WithParsedAsync<SearchVideos>(async command => await Search(
                youtube => youtube.SearchVideosAsync(command),
                result => DisplayVideoResult(result.Item1, result.Item2)));
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

        private static void DisplayVideoResult(string videoId, Caption[] captions, string videoPrefix = "")
        {
            var videoUrl = "https://youtu.be/" + videoId;
            Console.WriteLine(videoPrefix + videoUrl);

            foreach (var caption in captions)
            {
                Console.WriteLine(
                    "    {0}:{1} {2}    {3}?t={4}",
                    caption.At / 60,
                    caption.At % 60,
                    caption.Text,
                    videoUrl,
                    caption.At);
            }

            Console.WriteLine();
        }
    }
}