using CommandLine;

namespace SubTubular
{
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            //see https://github.com/commandlineparser/commandline
            Parser.Default.ParseArguments<SearchPlaylist>(args);
        }
    }
}