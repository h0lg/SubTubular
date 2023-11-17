using System.CommandLine;
using System.CommandLine.Invocation;
using SubTubular.Extensions;

namespace SubTubular.Shell;

static partial class Program
{
    private static async Task SearchAsync(SearchCommand command, string originalCommand)
    {
        CommandValidator.ValidateSearchCommand(command);

        await OutputAsync(command, originalCommand, async (youtube, cancellation, output) =>
        {
            var tracksWithErrors = new List<CaptionTrack>();

            await foreach (var result in youtube.SearchAsync(command, cancellation))
            {
                output.DisplayVideoResult(result);
                tracksWithErrors.AddRange(result.Video.CaptionTracks.Where(t => t.Error != null));
            }

            if (tracksWithErrors.Count > 0)
            {
                await WriteErrorLogAsync(originalCommand, tracksWithErrors.Select(t =>
@$"{t.LanguageName}: {t.ErrorMessage}

  {t.Url}

  {t.Error}").Join(AssemblyInfo.OutputSpacing), command.Describe());
            }
        });
    }

    static partial class CommandHandler
    {
        private static Command ConfigureSearchChannel(Func<SearchCommand, Task> handle)
        {
            Command searchChannel = new(Actions.search,
                "Searches the videos in a channel's Uploads playlist."
                + $" This is a glorified '{CommandGroups.playlist} {Actions.search}'.");

            searchChannel.AddAlias(Actions.search[..1]);
            Argument<string> alias = AddChannelAlias(searchChannel);
            (Option<IEnumerable<string>> query, Option<ushort> padding) = AddSearchCommandOptions(searchChannel);
            (Option<ushort> top, Option<IEnumerable<PlaylistLikeScope.OrderOptions>> orderBy, Option<float> cacheHours) = AddPlaylistLikeCommandOptions(searchChannel);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(searchChannel);

            searchChannel.SetHandler(async (ctx) => await handle(
                new SearchCommand { Scope = CreateChannelScope(ctx, alias, top, orderBy, cacheHours) }
                    .BindSearchOptions(ctx, query, padding)
                    .BindOuputOptions(ctx, html, fileOutputPath, show)));

            return searchChannel;
        }

        private static Command ConfigureSearchPlaylist(Func<SearchCommand, Task> handle)
        {
            Command searchPlaylist = new(Actions.search, "Searches the videos in a playlist.");
            searchPlaylist.AddAlias(Actions.search[..1]);
            Argument<string> playlist = AddPlaylistArgument(searchPlaylist);
            (Option<IEnumerable<string>> query, Option<ushort> padding) = AddSearchCommandOptions(searchPlaylist);
            (Option<ushort> top, Option<IEnumerable<PlaylistLikeScope.OrderOptions>> orderBy, Option<float> cacheHours) = AddPlaylistLikeCommandOptions(searchPlaylist);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(searchPlaylist);

            searchPlaylist.SetHandler(async (ctx) => await handle(
                new SearchCommand { Scope = CreatePlaylistScope(ctx, playlist, top, orderBy, cacheHours) }
                    .BindSearchOptions(ctx, query, padding)
                    .BindOuputOptions(ctx, html, fileOutputPath, show)));

            return searchPlaylist;
        }

        private static Command ConfigureSearchVideos(Func<SearchCommand, Task> handle)
        {
            Command searchVideos = new(Actions.search, "Searches the specified videos.");
            searchVideos.AddAlias(Actions.search[..1]);
            Argument<IEnumerable<string>> videos = AddVideosArgument(searchVideos);
            (Option<IEnumerable<string>> query, Option<ushort> padding) = AddSearchCommandOptions(searchVideos);
            (Option<bool> html, Option<string> fileOutputPath, Option<OutputCommand.Shows?> show) = AddOutputOptions(searchVideos);

            searchVideos.SetHandler(async (ctx) => await handle(
                new SearchCommand { Scope = CreateVideosScope(ctx, videos) }
                    .BindSearchOptions(ctx, query, padding)
                    .BindOuputOptions(ctx, html, fileOutputPath, show)));

            return searchVideos;
        }

        private static (Option<IEnumerable<string>> query, Option<ushort> padding) AddSearchCommandOptions(Command command)
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

            Option<ushort> padding = new(new[] { "--pad", "-p" }, () => 23,
                "How much context to pad a match in;"
                + " i.e. the minimum number of characters of the original description or subtitle track"
                + " to display before and after it.");

            command.AddOption(query);
            command.AddOption(padding);
            return (query, padding);
        }
    }
}

internal static partial class BindingExtensions
{
    /// <summary>Enables having a multi-word <see cref="SearchCommand.Query"/> (i.e. with spaces in between parts)
    /// without having to quote it and double-quote multi-word expressions within it.</summary>
    internal static SearchCommand BindSearchOptions(this SearchCommand search, InvocationContext ctx, Option<IEnumerable<string>> queryWords, Option<ushort> padding)
    {
        search.Query = ctx.Parsed(queryWords).Join(" ");
        search.Padding = ctx.Parsed(padding);
        return search;
    }
}