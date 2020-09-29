using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandLine;

namespace SubTubular
{
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            //see https://github.com/commandlineparser/commandline
            Parser.Default.ParseArguments<SearchPlaylist>(args).WithParsed(command =>
            {
                var fileStoragePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Assembly.GetEntryAssembly().GetName().Name);

                var youtube = new Youtube(new JsonFileDataStore(fileStoragePath));
                var captionsByVideos = youtube.SearchPlaylist(command);
                Console.WriteLine();

                foreach (var captionsByVideo in captionsByVideos)
                {
                    var videoUrl = "https://youtu.be/" + captionsByVideo.Key.Id;
                    Console.WriteLine("{0:g} {1}", captionsByVideo.Key.Uploaded, videoUrl);

                    foreach (var caption in captionsByVideo.Value)
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

                if (captionsByVideos.Any()) Console.WriteLine("All captions were downloaded to " + fileStoragePath);
            });
        }
    }
}