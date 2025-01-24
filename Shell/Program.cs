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

    private static async Task<int> Main(string[] args)
    {
        var originalCommand = $"> {AssemblyInfo.Name}.exe "
            // quote shell args including pipes to accurately represent the console command
            + args.Select(arg => arg.Contains('|') ? $"\"{arg.Replace("\"", "\"\"")}\"" : arg).Join(" ");

        try
        {
            var exitCode = await CommandInterpreter.ParseArgs(args, originalCommand);
            return (int)exitCode;
        }
        catch (Exception ex)
        {
            var causes = ex.GetRootCauses();

            if (causes.AreAll<OperationCanceledException>())
            {
                Console.WriteLine("The operation was canceled.");
                return (int)ExitCode.Canceled;
            }

            if (causes.All(c => c.IsInputError()))
            {
                foreach (var cause in causes)
                    WriteConsoleError(cause.Message);

                return (int)ExitCode.ValidationError;
            }

            if (causes.AreAll<HttpRequestException>())
                WriteConsoleError(causes.Select(c => c.Message)
                    .Prepend("Unexpected errors occurred loading data from YouTube. Try again later or with an updated version. ")
                    .Join(Environment.NewLine));
            else await WriteErrorLogAsync(originalCommand, ex.ToString());

            return (int)ExitCode.GenericError;
        }
    }

    private static void WriteConsoleError(string line)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(line);
        Console.ResetColor();
    }

    private static readonly string cacheFolder = Folder.GetPath(Folders.cache);
    internal static DataStore CreateDataStore() => new JsonFileDataStore(cacheFolder);
    private static VideoIndexRepository CreateVideoIndexRepo() => new(cacheFolder);

    private static async Task WriteErrorLogAsync(string originalCommand, string errors, string? name = null)
    {
        (var path, var report) = await ErrorLog.WriteAsync(errors, header: originalCommand, fileNameDescription: name);
        var fileWritten = path != null;

        if (fileWritten) WriteConsoleError("Errors were logged to " + path);
        else
        {
            WriteConsoleError("The following errors occurred and we were unable to write a log for them.");
            Console.WriteLine();
            WriteConsoleError(report);
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

enum ExitCode { Success = 0, GenericError = 1, ValidationError = 2, Canceled = 3 }
