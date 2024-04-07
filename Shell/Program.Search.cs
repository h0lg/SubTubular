using System.CommandLine;
using System.CommandLine.Invocation;
using SubTubular.Extensions;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task SearchAsync(SearchCommand command, string originalCommand)
    {
        CommandValidator.PrevalidateSearchCommand(command);

        await OutputAsync(command, originalCommand, async (youtube, outputs, cancellation) =>
        {
            var tracksWithErrors = new List<CaptionTrack>();

            await foreach (var result in youtube.SearchAsync(command, cancellation))
            {
                outputs.ForEach(o => o.WriteVideoResult(result, command.Padding));
                tracksWithErrors.AddRange(result.Video.CaptionTracks.Where(t => t.Error != null));
            }

            if (tracksWithErrors.Count > 0)
            {
                await WriteErrorLogAsync(originalCommand, name: command.Describe(),
                    errors: tracksWithErrors.Select(t =>
@$"{t.LanguageName}: {t.ErrorMessage}

  {t.Url}

  {t.Error}").Join(AssemblyInfo.OutputSpacing));
            }
        });
    }

    static partial class CommandHandler
    {
        private static Command ConfigureSearch(Func<SearchCommand, Task> search)
        {
            Command command = new(Actions.search, "Searches the videos in the specified scopes.");

            command.AddAlias(Actions.search[..1]); // first character
            var (channels, playlists, videos) = AddScopes(command);
            (Option<IEnumerable<string>> query, Option<ushort> padding, Option<IEnumerable<SearchCommand.OrderOptions>> orderBy) = AddSearchCommandOptions(command);
            (Option<ushort> top, Option<float> cacheHours) = AddPlaylistLikeCommandOptions(command);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(command);
            Option<bool> saveAsRecent = AddSaveAsRecent(command);

            command.SetHandler(async (ctx) => await search(
                new SearchCommand
                {
                    Channels = CreateChannelScopes(ctx, channels, top, cacheHours),
                    Playlists = CreatePlaylistScopes(ctx, playlists, top, cacheHours),
                    Videos = CreateVideosScope(ctx, videos)
                }
                .BindSearchOptions(ctx, query, padding, orderBy)
                .BindOuputOptions(ctx, html, fileOutputPath, show)
                .BindSaveAsRecent(ctx, saveAsRecent)));

            return command;
        }

        private static (Option<IEnumerable<string>> query, Option<ushort> padding, Option<IEnumerable<SearchCommand.OrderOptions>> orderBy) AddSearchCommandOptions(Command command)
        {
            Option<IEnumerable<string>> query = new(["--for", "-f"],
                "What to search for."
                + @" Quote ""multi-word phrases"". Single words are matched exactly by default,"
                + " ?fuzzy or with wild cards for s%ngle and multi* letters."
                + @" Combine multiple & terms | ""phrases or queries"" using AND '&' and OR '|'"
                + " and ( use | brackets | for ) & ( complex | expressions )."
                + $" You can restrict your search to the video '{nameof(Video.Title)}', '{nameof(Video.Description)}',"
                + $@" '{nameof(Video.Keywords)}' and/or '{nameof(CaptionTrack.Captions)}'; e.g. '{nameof(Video.Title)}=""click bait""'."
                + " Learn more about the query syntax at https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/ .")
            {
                AllowMultipleArgumentsPerToken = true,
                IsRequired = true
            };

            Option<ushort> padding = new(["--pad", "-p"], () => 23,
                "How much context to pad a match in;"
                + " i.e. the minimum number of characters of the original description or subtitle track"
                + " to display before and after it.");

            Option<IEnumerable<SearchCommand.OrderOptions>> orderBy = new([orderByName, "-r"], () => [SearchCommand.OrderOptions.score],
                $"Order the video search results by '{nameof(SearchCommand.OrderOptions.uploaded)}'"
                + $" or '{nameof(SearchCommand.OrderOptions.score)}' with '{nameof(SearchCommand.OrderOptions.asc)}' for ascending."
                + $" The default is descending (i.e. latest respectively highest first) and by '{nameof(SearchCommand.OrderOptions.score)}'."
                + $" Note that the order is only applied to the results with the search scope itself being limited by the '{topName}' parameter for playlists."
                + " Note also that for un-cached videos, this option is ignored in favor of outputting matches as soon as they're found"
                + " - but simply repeating the search will hit the cache and return them in the requested order.")
            { AllowMultipleArgumentsPerToken = true };

            command.AddOption(query);
            command.AddOption(padding);
            command.AddOption(orderBy);
            return (query, padding, orderBy);
        }
    }
}

internal static partial class BindingExtensions
{
    /// <summary>Enables having a multi-word <see cref="SearchCommand.Query"/> (i.e. with spaces in between parts)
    /// without having to quote it and double-quote multi-word expressions within it.</summary>
    internal static SearchCommand BindSearchOptions(this SearchCommand search, InvocationContext ctx,
        Option<IEnumerable<string>> queryWords, Option<ushort> padding, Option<IEnumerable<SearchCommand.OrderOptions>> orderBy)
    {
        search.Query = ctx.Parsed(queryWords)?.Join(" ");
        search.Padding = ctx.Parsed(padding);
        IEnumerable<SearchCommand.OrderOptions>? orders = ctx.Parsed(orderBy);
        if (orders != null) search.OrderBy = orders; // only override default if supplied
        return search;
    }
}