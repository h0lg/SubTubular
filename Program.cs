using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CommandLine;

namespace SubTubular
{
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            //see https://github.com/commandlineparser/commandline
            Parser.Default.ParseArguments<SearchPlaylist, SearchVideos>(args)
                .WithParsed<SearchPlaylist>(command => Search(
                    youtube => youtube.SearchPlaylist(command),
                    result => DisplayVideoResult(result.Key.Id, result.Value, result.Key.Uploaded.ToString("g") + " ")))
                .WithParsed<SearchVideos>(command => Search(
                    youtube => youtube.SearchVideos(command),
                    result => DisplayVideoResult(result.Key, result.Value)));
        }

        private static void Search<T>(Func<Youtube, IEnumerable<T>> getResults, Action<T> displayResult)
        {
            var fileStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Assembly.GetEntryAssembly().GetName().Name);

            var youtube = new Youtube(new JsonFileDataStore(fileStoragePath));
            Console.WriteLine();
            var hasResult = false;

            foreach (var result in getResults(youtube))
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