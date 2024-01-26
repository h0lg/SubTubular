﻿using System.CommandLine;
using System.CommandLine.Invocation;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task OutputAsync(OutputCommand command, string originalCommand,
        Func<Youtube, CancellationToken, OutputWriter, Task> runCommand)
    {
        //inspired by https://johnthiriet.com/cancel-asynchronous-operation-in-csharp/
        using var cancellation = new CancellationTokenSource();
        var running = true;

        var cancel = Task.Run(async () => //start in background, don't wait for completion
        {
            Console.WriteLine("Press any key to cancel");
            Console.WriteLine();

            /* wait for key or operation to finish, non-blockingly; but only as long as to not cause perceivable lag
                inspired by https://stackoverflow.com/a/5620647 and https://stackoverflow.com/a/23628232 */
            while (running && !Console.KeyAvailable) await Task.Delay(200, cancellation.Token);

            if (Console.KeyAvailable) Console.ReadKey(true); //consume cancel key without displaying it
            if (running) cancellation.Cancel();
        });

        var cacheFolder = Folder.GetPath(Folders.cache);
        DataStore dataStore = new JsonFileDataStore(cacheFolder);
        var youtube = new Youtube(dataStore, new VideoIndexRepository(cacheFolder));

        if (command.Scope is ChannelScope channel)
            await CommandValidator.RemoteValidateChannelAsync(channel, youtube.Client, dataStore, cancellation.Token);

        using (var output = new OutputWriter(command))
        {
            output.WriteHeader(originalCommand);

            try
            {
                /*  passing token into command for it to react to cancellation,
                    see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables */
                await runCommand(youtube, cancellation.Token, output);
            }
            catch (OperationCanceledException) { Console.WriteLine("The operation was cancelled."); }
            finally // write output file even if exception occurs
            {
                if (output.WroteResults) // if we displayed a result before running into an error
                {
                    // only writes an output file if command requires it
                    var path = await output.WriteOutputFile(() => Folder.GetPath(Folders.output));

                    if (path != null)
                    {
                        Console.WriteLine("Results were written to " + path);

                        // spare the user some file browsing
                        if (command.Show == OutputCommand.Shows.file) ShellCommands.OpenFile(path);
                        if (command.Show == OutputCommand.Shows.folder) ShellCommands.ExploreFolder(path);
                    }
                }
            }
        }

        running = false; // to let the cancel task complete if operation did before it
        await cancel; // just to rethrow possible exceptions
    }

    static partial class CommandHandler
    {
        private static (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) AddOutputOptions(Command command)
        {
            const string htmlName = "--html", outputPathName = "--out",
                existingFilesAreOverWritten = " Existing files with the same name will be overwritten.";

            Option<bool> html = new([htmlName, "-m"],
                "If set, outputs the highlighted search result in an HTML file including hyperlinks for easy navigation."
                + $" The output path can be configured in the '{outputPathName}' parameter."
                + " Omitting it will save the file into the default 'output' folder - named according to your search parameters."
                + existingFilesAreOverWritten);

            Option<string> fileOutputPath = new([outputPathName, "-o"],
                $"Writes the search results to a file, the format of which is either text or HTML depending on the '{htmlName}' flag."
                + " Supply either a file or folder path. If the path doesn't contain a file name, the file will be named according to your search parameters."
                + existingFilesAreOverWritten);

            Option<OutputCommand.Shows?> show = new(["--show", "-s"], "The output to open if a file was written.");

            command.AddOption(html);
            command.AddOption(fileOutputPath);
            command.AddOption(show);
            return (html, fileOutputPath, show);
        }
    }
}

internal static partial class BindingExtensions
{
    internal static T BindOuputOptions<T>(this T command, InvocationContext ctx,
        Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) where T : OutputCommand
    {
        command.OutputHtml = ctx.Parsed(html);
        command.FileOutputPath = ctx.Parsed(fileOutputPath);
        command.Show = ctx.Parsed(show);
        return command;
    }
}