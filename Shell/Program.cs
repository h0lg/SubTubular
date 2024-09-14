using Lifti;
using SubTubular.Extensions;

namespace SubTubular.Shell;

internal static partial class Program
{
    internal const string AsciiHeading = @"
   _____       __  ______      __          __
  / ___/__  __/ /_/_  __/_  __/ /_  __  __/ /___ ______
  \__ \/ / / / __ \/ / / / / / __ \/ / / / / __ `/ ___/
 ___/ / /_/ / /_/ / / / /_/ / /_/ / /_/ / / /_/ / /
/____/\__,_/_.___/_/  \__,_/_.___/\__,_/_/\__,_/_/

"; //from http://www.patorjk.com/software/taag/#p=display&f=Slant&t=SubTubular

    private static async Task Main(string[] args)
    {
        var originalCommand = $"> {AssemblyInfo.Name}.exe "
            // quote shell args including pipes to accurately represent the console command
            + args.Select(arg => arg.Contains('|') ? $"\"{arg.Replace("\"", "\"\"")}\"" : arg).Join(" ");

        try { await CommandHandler.HandleArgs(args, originalCommand); }
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

    private static readonly string cacheFolder = Folder.GetPath(Folders.cache);
    internal static DataStore CreateDataStore() => new JsonFileDataStore(cacheFolder);
    private static VideoIndexRepository CreateVideoIndexRepo() => new(cacheFolder);

    private static async Task WriteErrorLogAsync(string originalCommand, string errors, string? name = null)
    {
        (var path, var report) = await ErrorLog.WriteAsync(errors, header: originalCommand, fileNameDescription: name);
        var fileWritten = path != null;

        if (fileWritten) Console.WriteLine("Errors were logged to " + path);
        else
        {
            Console.WriteLine("The following errors occurred and we were unable to write a log for them.");
            Console.WriteLine();
            Console.Error.WriteLine(report);
        }

        Console.WriteLine();

        Console.WriteLine($"Try 'release --list' or check {AssemblyInfo.ReleasesUrl} for a {AssemblyInfo.Name} version"
            + $" newer than {AssemblyInfo.GetProductVersion()} that may have fixed this"
            + $" or {AssemblyInfo.IssuesUrl} for existing reports of this error and maybe a solution or work-around."
            + " If you can reproduce this error in the latest version, reporting it there is your best chance at getting it fixed."
            + " If you do, make sure to include the original command or parameters to reproduce it,"
            + $" any exception details that have not already been shared and the OS/.NET/{AssemblyInfo.Name} version you're on."
            + $" You'll find all that in the error {(fileWritten ? "log file" : "output above")}.");
    }
}