using System.CommandLine;
using System.CommandLine.Invocation;
using SubTubular.Extensions;

namespace SubTubular.Shell;

static partial class CommandInterpreter
{
    private const string clearCacheCommand = "clear-cache",
        quoteIdsStartingWithDash = " Note that if the video ID starts with a dash, you have to quote it"
            + @" like ""-1a2b3c4d5e"" or use the entire URL to prevent it from being misinterpreted as a command option.";

    internal static async Task<ExitCode> ParseArgs(string[] args, string originalCommand)
    {
        Task search(SearchCommand cmd, CancellationToken token) => Program.SearchAsync(cmd, originalCommand, token);
        Task listKeywords(ListKeywords cmd, CancellationToken token) => Program.ListKeywordsAsync(cmd, originalCommand, token);

        RootCommand root = new(AssemblyInfo.Title);
        // see https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax#directives
        // and https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax#the-diagram-directive
        root.Directives.Add(new DiagramDirective());

        // see https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax#subcommands
        root.Subcommands.Add(ConfigureSearch(search));
        root.Subcommands.Add(ConfigureListKeywords(listKeywords));
        root.Subcommands.Add(ConfigureClearCache(Program.ApplyClearCacheAsync));
        root.Subcommands.Add(ConfigureRelease());
        root.Subcommands.Add(ConfigureOpen());
        root.Subcommands.Add(ConfigureRecent(search, listKeywords));

        CommandLineConfiguration config = new(root)
        {
            EnableDefaultExceptionHandler = false // to throw exceptions instead of garbling them into an exit code
        };

        ParseResult parsed = config.Parse(args);

        try
        {
            var exit = await parsed.InvokeAsync();

            /*  parser errors are printed by invocation above and return a non-zero exit code - no need to check it
             *  see https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-parse-and-invoke#parse-errors */
            if (parsed.Action is ParseErrorAction) return ExitCode.ValidationError;

            if (Enum.IsDefined(typeof(ExitCode), exit)) return (ExitCode)exit; // translate known exit code
            else return ExitCode.GenericError; // unify unknown exit codes
        }
        catch (Exception ex)
        {
            var causes = ex.GetRootCauses().ToArray();

            foreach (var cause in causes)
                ColorShell.WriteErrorLine(cause.Message);

            if (causes.HaveInputError()) return ExitCode.ValidationError;
            if (causes.AreAllCancelations()) return ExitCode.Canceled;
            return ExitCode.GenericError; //or better throw?
        }
    }

    private static Command ConfigureOpen()
    {
        Command open = new("open", "Opens app-related folders in a file browser.");
        open.Aliases.Add("o");

        Argument<Folders> folder = new("folder") { Description = "The folder to open." };
        open.Arguments.Add(folder);

        open.SetAction(parsed => ShellCommands.ExploreFolder(Folder.GetPath(parsed.GetValue(folder))));
        return open;
    }
}
