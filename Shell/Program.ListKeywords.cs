using System.CommandLine;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task ListKeywordsAsync(ListKeywords command, string originalCommand)
    {
        CommandValidator.PrevalidateScopes(command);

        await OutputAsync(command, originalCommand, async (youtube, outputs, cancellation) =>
        {
            Dictionary<string, ushort> keywords = [];

            await foreach (var (keyword, videoId, scope) in youtube.ListKeywordsAsync(command, cancellation))
                if (keywords.ContainsKey(keyword)) keywords[keyword]++;
                else keywords.Add(keyword, 1);

            if (keywords.Any()) outputs.ForEach(o => o.ListKeywords(keywords));
            else Console.WriteLine("Found no keywords."); // any file output wouldn't be saved without results anyway
        });
    }

    static partial class CommandHandler
    {
        private static Command ConfigureListKeywords(Func<ListKeywords, Task> listKeywords)
        {
            Command command = new(Actions.listKeywords,
                "List the keywords in a channel's Uploads playlist."
                + $" This is a glorified '{CommandGroups.playlist} {Actions.listKeywords}'.");

            command.AddAlias(Actions.listKeywords[..1]); // first character
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