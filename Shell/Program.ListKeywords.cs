using System.CommandLine;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task ListKeywordsAsync(ListKeywords command, string originalCommand)
    {
        CommandValidator.ValidateCommandScope(command.Scope);

        await OutputAsync(command, originalCommand, async (youtube, cancellation, output) =>
        {
            var keywords = await youtube.ListKeywordsAsync(command, cancellation);

            if (keywords.Any()) output.ListKeywords(keywords);
            else Console.WriteLine("Found no keywords.");
        });
    }

    static partial class CommandHandler
    {
        private static Command ConfigureListChannelKeywords(Func<ListKeywords, Task> listKeywords)
        {
            Command listChannelKeywords = new(Actions.listKeywords,
                "List the keywords in a channel's Uploads playlist."
                + $" This is a glorified '{CommandGroups.playlist} {Actions.listKeywords}'.");

            listChannelKeywords.AddAlias(Actions.listKeywords[..1]);
            Argument<string> alias = AddChannelAlias(listChannelKeywords);
            (Option<ushort> top, Option<float> cacheHours) = AddPlaylistLikeCommandOptions(listChannelKeywords);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(listChannelKeywords);

            listChannelKeywords.SetHandler(async (ctx) => await listKeywords(
                new ListKeywords { Scope = CreateChannelScope(ctx, alias, top, cacheHours) }
                    .BindOuputOptions(ctx, html, fileOutputPath, show)));

            return listChannelKeywords;
        }

        private static Command ConfigureListPlaylistKeywords(Func<ListKeywords, Task> listKeywords)
        {
            Command listPlaylistKeywords = new(Actions.listKeywords, "Lists the keywords of the videos in a playlist.");
            listPlaylistKeywords.AddAlias(Actions.listKeywords[..1]);
            Argument<string> playlist = AddPlaylistArgument(listPlaylistKeywords);
            (Option<ushort> top, Option<float> cacheHours) = AddPlaylistLikeCommandOptions(listPlaylistKeywords);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(listPlaylistKeywords);

            listPlaylistKeywords.SetHandler(async (ctx) => await listKeywords(
                new ListKeywords { Scope = CreatePlaylistScope(ctx, playlist, top, cacheHours) }
                    .BindOuputOptions(ctx, html, fileOutputPath, show)));

            return listPlaylistKeywords;
        }

        private static Command ConfigureListVideoKeywords(Func<ListKeywords, Task> listKeywords)
        {
            Command listVideoKeywords = new(Actions.listKeywords, "Lists the keywords of the specified videos.");
            listVideoKeywords.AddAlias(Actions.listKeywords[..1]);
            Argument<IEnumerable<string>> videos = AddVideosArgument(listVideoKeywords);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(listVideoKeywords);

            listVideoKeywords.SetHandler(async (ctx) => await listKeywords(
                new ListKeywords { Scope = CreateVideosScope(ctx, videos) }
                    .BindOuputOptions(ctx, html, fileOutputPath, show)));

            return listVideoKeywords;
        }
    }
}