﻿using SubTubular.Extensions;

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
            var causes = ex.GetRootCauses().ToArray();

            if (causes.AreAllCancelations())
            {
                Console.WriteLine("The operation was canceled.");
                return (int)ExitCode.Canceled;
            }

            if (causes.All(c => c.IsInputError()))
            {
                WriteConsoleError("The command ran into input errors:");

                // summarize the errors, OnScopeNotified in OutputAsync already wrote every one of them when they occurred
                foreach (var error in causes.Select(ex => ex.Message).Distinct())
                    WriteConsoleError(error);

                return (int)ExitCode.ValidationError;
            }

            if (causes.AreAll<HttpRequestException>())
                WriteConsoleError(causes.Select(c => c.Message)
                    .Prepend("Unexpected errors occurred loading data from YouTube. Try again later or with an updated version. ")
                    .Join(Environment.NewLine));
            else await WriteErrorLogAsync(originalCommand, ex is ErrorLogException ? ex.Message : ex.ToString());

            return (int)ExitCode.GenericError;
        }
    }

    private static void WriteConsoleError(string line) => ColorShell.WriteErrorLine(line);

    private static readonly string cacheFolder = Folder.GetPath(Folders.cache);
    internal static DataStore CreateDataStore() => new JsonFileDataStore(cacheFolder);
    private static VideoIndexRepository CreateVideoIndexRepo() => new(cacheFolder);

    private static async Task WriteErrorLogAsync(string originalCommand, string errors)
    {
        (var path, var report) = await ErrorLog.WriteAsync(errors, header: originalCommand);
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

    /// <summary>A special exception containing combined error details (including stack traces)
    /// in its <see cref="Exception.Message"/>.
    /// Its stack trace can be ignored because it is thrown intentionally to log the details globally.</summary>
    internal class ErrorLogException : Exception
    {
        public ErrorLogException() { }
        public ErrorLogException(string? message) : base(message) { }
        public ErrorLogException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}

enum ExitCode { Success = 0, GenericError = 1, ValidationError = 2, Canceled = 3 }
