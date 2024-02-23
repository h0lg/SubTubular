using System.CommandLine;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task ListKeywordsAsync(ListKeywords command, string originalCommand)
    {
        CommandValidator.PrevalidateScopes(command);

        await OutputAsync(command, originalCommand, async (youtube, cancellation, outputs) =>
        {
            var resultDisplayed = false;
            Dictionary<string, ushort> keywords = [];

            await foreach (var (keyword, videoId, scope) in youtube.ListKeywordsAsync(command).WithCancellation(cancellation))
                if (keywords.ContainsKey(keyword)) keywords[keyword]++;
                else keywords.Add(keyword, 1);

            if (keywords.Any())
            {
                foreach (var output in outputs) output.ListKeywords(keywords);
                resultDisplayed = true;
            }
            else Console.WriteLine("Found no keywords.");

            return resultDisplayed;
        });
    }

    static partial class CommandHandler
    {
        private static Command ConfigureListKeywords(Func<ListKeywords, Task> listKeywords)
        {
            Command command = new(Actions.listKeywords,
                "List the keywords in a channel's Uploads playlist."
                + $" This is a glorified '{CommandGroups.playlist} {Actions.listKeywords}'.");

            command.AddAlias(Actions.listKeywords[..1]);
            var (channels, playlists, videos) = AddScopes(command);
            (Option<ushort> top, Option<float> cacheHours) = AddPlaylistLikeCommandOptions(command);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(command);

            command.SetHandler(async (ctx) => await listKeywords(new ListKeywords
            {
                Channels = CreateChannelScopes(ctx, channels, top, cacheHours),
                Playlists = CreatePlaylistScopes(ctx, playlists, top, cacheHours),
                Videos = CreateVideosScope(ctx, videos)
            }.BindOuputOptions(ctx, html, fileOutputPath, show)));

            return command;
        }
    }
}