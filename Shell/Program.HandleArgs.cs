using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace SubTubular.Shell;

static partial class Program
{
    static partial class CommandHandler
    {
        private const string clearCacheCommand = "clear-cache",
            quoteIdsStartingWithDash = " Note that if the video ID starts with a dash, you have to quote it"
                + @" like ""-1a2b3c4d5e"" or use the entire URL to prevent it from being misinterpreted as a command option.";

        internal static async Task HandleArgs(string[] args, string originalCommand)
        {
            Task search(SearchCommand cmd) => SearchAsync(cmd, originalCommand);
            Task listKeywords(ListKeywords cmd) => ListKeywordsAsync(cmd, originalCommand);

            RootCommand root = new(AssemblyInfo.Title);

            // see https://learn.microsoft.com/en-us/dotnet/standard/commandline/define-commands#define-subcommands
            Command channel = new(CommandGroups.channel, "search a channel or list its keywords");
            channel.AddAlias("c");
            root.AddCommand(channel);
            channel.AddCommand(ConfigureSearchChannel(search));
            channel.AddCommand(ConfigureListChannelKeywords(listKeywords));

            Command playlist = new(CommandGroups.playlist, "search a playlist or list its keywords");
            playlist.AddAlias("p");
            root.AddCommand(playlist);
            playlist.AddCommand(ConfigureSearchPlaylist(search));
            playlist.AddCommand(ConfigureListPlaylistKeywords(listKeywords));

            Command videos = new(CommandGroups.videos, "search videos or list their keywords");
            videos.AddAlias("v");
            root.AddCommand(videos);
            videos.AddCommand(ConfigureSearchVideos(search));
            videos.AddCommand(ConfigureListVideoKeywords(listKeywords));

            root.AddCommand(ConfigureClearCache(ApplyClearCacheAsync));
            root.AddCommand(ConfigureRelease());
            root.AddCommand(ConfigureOpen());

            Parser parser = new CommandLineBuilder(root).UseDefaults()
                // see https://learn.microsoft.com/en-us/dotnet/standard/commandline/customize-help
                .UseHelp(ctx => ctx.HelpBuilder.CustomizeLayout(context =>
                {
                    var layout = HelpBuilder.Default.GetLayout();

                    if (context.Command == root)
                    {
                        layout = layout
                            .Skip(1) // Skip the default command description section.
                            .Prepend(_ =>
                            {
                                // enhance heading for branding
                                Console.WriteLine(asciiHeading + root.Description + " " + AssemblyInfo.InformationalVersion);
                                Console.WriteLine(AssemblyInfo.Copyright);
                            });
                    }

                    return layout.Append(_ => Console.WriteLine(Environment.NewLine + $"See {AssemblyInfo.RepoUrl} for more info."));
                }))
                .Build();

            await parser.InvokeAsync(args);
        }

        private static Command ConfigureOpen()
        {
            Command open = new("open", "Opens app-related folders in a file browser.");
            open.AddAlias("o");

            Argument<Folders> folder = new("folder", "The folder to open.");
            open.AddArgument(folder);

            open.SetHandler(folder => ShellCommands.ExploreFolder(Folder.GetPath(folder)), folder);
            return open;
        }

        private static class CommandGroups
        {
            internal const string channel = "channel", playlist = "playlist", videos = "videos";
        }

        private static class Actions
        {
            internal const string search = "search", listKeywords = "keywords";
        }
    }
}

internal static partial class BindingExtensions
{
    internal static T Parsed<T>(this InvocationContext context, Argument<T> arg)
        => context.ParseResult.GetValueForArgument(arg);

    internal static T Parsed<T>(this InvocationContext context, Option<T> option)
        => context.ParseResult.GetValueForOption(option);
}