using System.CommandLine;

namespace SubTubular.Shell;

static partial class Program
{
    internal static async Task ListKeywordsAsync(ListKeywords command, string originalCommand)
    {
        Prevalidate.Scopes(command);

        await OutputAsync(command, originalCommand, async (youtube, outputs, cancellation) =>
        {
            Dictionary<CommandScope, Dictionary<string, List<string>>> scopes = [];

            await foreach (var (keywords, videoId, scope) in youtube.ListKeywordsAsync(command, cancellation))
                Youtube.AggregateKeywords(keywords, videoId, scope, scopes);

            if (scopes.Any())
            {
                var countedKeywords = Youtube.CountKeywordVideos(scopes);
                outputs.ForEach(o => o.ListKeywords(countedKeywords));
            }
            else Console.WriteLine("Found no keywords."); // any file output wouldn't be saved without results anyway
        });
    }
}

static partial class CommandInterpreter
{
    private static Command ConfigureListKeywords(Func<ListKeywords, Task> listKeywords)
    {
        Command command = new(Actions.listKeywords, ListKeywords.Description);
        command.AddAlias(Actions.listKeywords[..1]); // first character

        var (channels, playlists, videos) = AddScopes(command);
        (Option<ushort> skip, Option<ushort> take, Option<float> cacheHours) = AddPlaylistLikeCommandOptions(command);
        (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(command);
        Option<bool> saveAsRecent = AddSaveAsRecent(command);

        command.SetHandler(async (ctx) => await listKeywords(
            new ListKeywords
            {
                Channels = CreateChannelScopes(ctx, channels, skip, take, cacheHours),
                Playlists = CreatePlaylistScopes(ctx, playlists, skip, take, cacheHours),
                Videos = CreateVideosScope(ctx, videos)
            }
            .BindOuputOptions(ctx, html, fileOutputPath, show)
            .BindSaveAsRecent(ctx, saveAsRecent)));

        return command;
    }
}