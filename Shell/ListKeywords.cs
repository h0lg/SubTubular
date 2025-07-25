using System.CommandLine;

namespace SubTubular.Shell;

static partial class Program
{
    internal static async Task ListKeywordsAsync(ListKeywords command, string originalCommand, CancellationToken token)
    {
        Prevalidate.Scopes(command);

        await OutputAsync(command, originalCommand, async (youtube, outputs, cancellation) =>
        {
            Dictionary<CommandScope, Dictionary<string, List<string>>> scopes = [];

            await foreach (var (keywords, videoId, scope) in youtube.ListKeywordsAsync(command, cancellation))
                Youtube.AggregateKeywords(keywords, videoId, scope, scopes);

            if (scopes.Count > 0)
            {
                var countedKeywords = Youtube.CountKeywordVideos(scopes);
                outputs.ForEach(o => o.ListKeywords(countedKeywords));
            }
            else Console.WriteLine("Found no keywords."); // any file output wouldn't be saved without results anyway
        }, token);
    }
}

static partial class CommandInterpreter
{
    private static Command ConfigureListKeywords(Func<ListKeywords, CancellationToken, Task> listKeywords)
    {
        Command command = new(Actions.listKeywords, ListKeywords.Description);
        command.Aliases.Add(Actions.listKeywords[..1]); // first character

        var (channels, playlists, videos) = AddScopes(command);
        (Option<IEnumerable<ushort>?> skip, Option<IEnumerable<ushort>?> take, Option<IEnumerable<float>?> cacheHours) = AddPlaylistLikeCommandOptions(command);
        (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(command);
        Option<bool> saveAsRecent = AddSaveAsRecent(command);

        command.SetAction(async (result, token) => await listKeywords(new ListKeywords()
            .BindScopes(result, videos, channels, playlists, skip, take, cacheHours)
            .BindOuputOptions(result, html, fileOutputPath, show)
            .BindSaveAsRecent(result, saveAsRecent), token));

        return command;
    }
}