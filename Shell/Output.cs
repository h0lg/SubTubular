using System.Collections.Concurrent;
using System.CommandLine;
using SubTubular.Extensions;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task OutputAsync(OutputCommand command, string originalCommand,
        Func<Youtube, List<OutputWriter>, CancellationToken, Task> runCommand,
        CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        var running = true;

        //inspired by https://johnthiriet.com/cancel-asynchronous-operation-in-csharp/
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

        DataStore dataStore = CreateDataStore();
        var youtube = new Youtube(dataStore, CreateVideoIndexRepo());
        await RemoteValidate.ScopesAsync(command, youtube, dataStore, cancellation.Token);

        if (command.SaveAsRecent)
        {
            var commands = await RecentCommands.ListAsync();
            commands.AddOrUpdate(command);
            await RecentCommands.SaveAsync(commands);
        }

        List<OutputWriter> outputs = [new ConsoleOutputWriter(command)];

        if (command.OutputHtml) outputs.Add(new HtmlOutputWriter(command));
        else if (command.HasOutputPath(out var _path)) outputs.Add(new TextOutputWriter(command));

        outputs.ForEach(output =>
        {
            if (output is FileOutputWriter) output.WriteLine(originalCommand); // for reference and repeating
            output.WriteHeader();
        });

        ConcurrentBag<string> reportableErrors = [];

        foreach (var (scope, captionTrackDlStates) in command.GetCaptionTrackDownloadStatus())
        {
            var notifications = captionTrackDlStates.Irregular().AsNotifications();

            if (notifications.Length > 0)
                foreach (var ntf in notifications) OnScopeNotified(scope, ntf);
        }

        // set up async notification channel
        command.OnScopeNotification(OnScopeNotified);

        try
        {
            /*  passing token into command for it to react to cancellation,
                see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables */
            await runCommand(youtube, outputs, cancellation.Token);
        }
        // record unexpected error here to have it in the same log file as the scope errors
        catch (Exception ex) when (ex.GetRootCauses().AnyNeedReporting())
        {
            reportableErrors.Add($"{DateTime.Now:O} {ex}");
            throw new OperationCanceledException(); // throw an error ignored upstream to signal execution error without triggering duplicate logging
        }
        finally // write output file even if exception occurs
        {
            if (outputs.Any(o => o.WroteResults)) // if we displayed a result before running into an error
            {
                // only writes an output file if command requires it
                var fileOutput = outputs.OfType<FileOutputWriter>().SingleOrDefault();
                var outputPath = fileOutput == null ? null : await fileOutput.SaveFile();

                if (outputPath != null)
                {
                    Console.WriteLine("Results were written to " + outputPath);

                    // spare the user some file browsing
                    if (command.Show == OutputCommand.Shows.file) ShellCommands.OpenFile(outputPath);
                    if (command.Show == OutputCommand.Shows.folder) ShellCommands.ExploreFolder(outputPath);
                }
            }

            if (!reportableErrors.IsEmpty) await WriteErrorLogAsync(originalCommand, reportableErrors.Join(ErrorLog.OutputSpacing), command.Describe());

            foreach (var output in outputs.OfType<IDisposable>()) output.Dispose();
            running = false; // to let the cancel task complete if operation did before it
            await cancel; // just to rethrow possible exceptions
        }

        void OnScopeNotified(CommandScope scope, CommandScope.Notification notification) => outputs.ForEach(output =>
        {
            output.WriteLine();
            var titleAndScope = notification.Title + " in " + scope.Describe(inDetail: false).Join(" ");
            bool hasErrors = notification.Errors.HasAny();
            Action<string> write = hasErrors ? text => output.WriteErrorLine(text) : text => output.WriteNotificationLine(text);
            write(titleAndScope);
            Video? video = notification.Video;
            if (video != null) write($"Video: {video.Title} {Youtube.GetVideoUrl(video.Id)}");
            if (notification.Message.IsNonEmpty()) write(notification.Message!);

            if (hasErrors)
            {
                var causes = notification.Errors!.GetRootCauses().ToArray();

                if (causes.AnyNeedReporting())
                {
                    // collect error details for log
                    var errorDetails = causes.Select(e => e.ToString())
                        .Prepend(notification.Message)
                        .Prepend($"{DateTime.Now:O} {titleAndScope}")
                        .WithValue().Join(ErrorLog.OutputSpacing);

                    reportableErrors.Add(errorDetails);
                }

                // output messages immediately
                foreach (var error in causes)
                    write(error.Message);
            }

            output.WriteLine();
        });
    }
}

static partial class CommandInterpreter
{
    private static Option<bool> AddSaveAsRecent(Command command)
    {
        Option<bool> saveAsRecent = new("--recent", "-rc")
        {
            Description = "Unless set explicitly to 'false', saves this command into the recent command list to enable re-running it later.",
            DefaultValueFactory = _ => true
        };

        command.Options.Add(saveAsRecent);
        return saveAsRecent;
    }

    private static (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) AddOutputOptions(Command command)
    {
        const string htmlName = "--html", outputPathName = "--out";

        Option<bool> html = new(htmlName, "-m")
        {
            Description = "If set, outputs the highlighted search result in an HTML file including hyperlinks for easy navigation."
                + $" The output path can be configured in the '{outputPathName}' parameter."
                + " Omitting it will save the file into the default 'output' folder - named according to your search parameters."
                + OutputCommand.ExistingFilesAreOverWritten
        };

        Option<string> fileOutputPath = new(outputPathName, "-o")
        {
            Description =
                $"Writes the search results to a file, the format of which is either text or HTML depending on the '{htmlName}' flag. "
                + OutputCommand.FileOutputPathHint + OutputCommand.ExistingFilesAreOverWritten
        };

        Option<OutputCommand.Shows?> show = new("--show", "-s") { Description = "The output to open if a file was written." };

        command.Options.Add(html);
        command.Options.Add(fileOutputPath);
        command.Options.Add(show);
        return (html, fileOutputPath, show);
    }
}

internal static partial class BindingExtensions
{
    internal static T BindSaveAsRecent<T>(this T command, ParseResult parsed, Option<bool> saveAsRecent) where T : OutputCommand
    {
        command.SaveAsRecent = parsed.GetValue(saveAsRecent);
        return command;
    }

    internal static T BindOuputOptions<T>(this T command, ParseResult parsed,
        Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) where T : OutputCommand
    {
        command.OutputHtml = parsed.GetValue(html);
        command.FileOutputPath = parsed.GetValue(fileOutputPath);
        command.Show = parsed.GetValue(show);
        return command;
    }
}
