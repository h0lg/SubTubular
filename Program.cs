using System;
using CommandLine;

namespace SubTubular
{
    /// <summary>
    /// YouTube Data API v3 sample: retrieve my uploads.
    /// Relies on the Google APIs Client Library for .NET, v1.7.0 or higher.
    /// See https://developers.google.com/api-client-library/dotnet/get_started
    /// </summary>
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            //see https://github.com/commandlineparser/commandline
            var parserResult = Parser.Default.ParseArguments<SearchPlaylist>(args);

            parserResult.WithParsed(command =>
            {
                var apiKey = YouTube.GetApiKey();

                if (apiKey == null)
                {
                    Console.Write("Please provide an API key:");
                    var key = Console.ReadLine().Trim();
                    YouTube.SetApiKey(key);
                }

                var youTube = new YouTube(apiKey);
                var videoIds = youTube.SearchPlaylist(command);

                foreach (var id in videoIds)
                {
                    Console.WriteLine("https://youtu.be/" + id);
                }
            });
        }
    }
}