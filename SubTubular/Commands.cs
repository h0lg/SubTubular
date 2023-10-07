using CommandLine;

namespace SubTubular;

internal abstract class SearchCommand
{
    private const string html = "html", outputPath = "out", query = "query", @for = "for",
        existingFilesAreOverWritten = " Existing files with the same name will be overwritten.";

    /// <summary>Enables having a multi-word <see cref="Query"/> (i.e. with spaces in between parts)
    /// without having to quote it and double-quote multi-word expressions within it.</summary>
    [Option('f', @for, Group = query, HelpText = "What to search for."
        + @" Quote ""multi-word phrases"". Single words are matched exactly by default,"
        + " ?fuzzy or with wild cards for s%ngle and multi* letters."
        + @" Combine multiple & terms | ""phrases or queries"" using AND '&' and OR '|'"
        + " and ( use | brackets | for ) & ( complex | expressions )."
        + $" You can restrict your search to the video '{nameof(Video.Title)}', '{nameof(Video.Description)}',"
        + $@" '{nameof(Video.Keywords)}' and/or language-specific captions; e.g. '{nameof(Video.Title)}=""click bait""'."
        + " Learn more about the query syntax at https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/ .")]
    public IEnumerable<string> QueryWords { set { Query = value.Join(" "); } }

    public string Query { get; private set; }

    [Option('k', "keywords", Group = query,
        HelpText = "Lists the keywords the videos in scope are tagged with including their number of occurrences.")]
    public bool ListKeywords { get; set; }

    [Option('p', "pad", Default = (ushort)23, HelpText = "How much context to pad a match in;"
        + " i.e. the minimum number of characters of the original description or subtitle track"
        + " to display before and after it.")]
    public ushort Padding { get; set; }

    [Option('m', html,
        HelpText = "If set, outputs the highlighted search result in an HTML file including hyperlinks for easy navigation."
            + $" The output path can be configured in the '{outputPath}' parameter."
            + " Omitting it will save the file into the default 'output' folder - named according to your search parameters."
            + existingFilesAreOverWritten)]
    public bool OutputHtml { get; set; }

    [Option('o', outputPath,
        HelpText = $"Writes the search results to a file, the format of which is either text or HTML depending on the '{html}' flag."
            + $" Supply either a file or folder path. If the path doesn't contain a file name, the file will be named according to your search parameters."
            + existingFilesAreOverWritten)]
    public string FileOutputPath { get; set; }

    [Option('s', "show", HelpText = "The output to open if a file was written.")]
    public Shows? Show { get; set; }

    internal IEnumerable<string> ValidUrls { get; set; }

    protected abstract string FormatInternal();
    internal string GetQueryName() => $"'--{@for}' option";

    internal string Describe() => ListKeywords ? ("listing keywords in " + FormatInternal())
        : ("searching " + FormatInternal() + " for " + Query);

    public enum Shows { file, folder }
}

internal abstract class SearchPlaylistCommand : SearchCommand
{
    private const string top = "top", orderBy = "order-by";

    [Option('t', top, Default = (ushort)50,
        HelpText = "The number of videos to search, counted from the top of the playlist;"
            + " effectively limiting the search scope to the top partition of it."
            + " You may want to gradually increase this to include all videos in the list while you're refining your query."
            + $" Note that the special Uploads playlist of a channel is sorted latest '{nameof(OrderOptions.uploaded)}' first,"
            + " but custom playlists may be sorted differently. Keep that in mind if you don't find what you're looking for"
            + $" and when using '--{orderBy}' (which is only applied to the results) with '{nameof(OrderOptions.uploaded)}' on custom playlists.")]
    public ushort Top { get; set; }

    [Option('r', orderBy, HelpText = $"Order the video search results by '{nameof(OrderOptions.uploaded)}'"
        + $" or '{nameof(OrderOptions.score)}' with '{nameof(OrderOptions.asc)}' for ascending."
        + $" The default is descending (i.e. latest respectively highest first) and by '{nameof(OrderOptions.score)}'."
        + $" Note that the order is only applied to the results with the search scope itself being limited by the '--{top}' parameter."
        + " Note also that for un-cached videos, this option is ignored in favor of outputting matches as soon as they're found"
        + " - but simply repeating the search will hit the cache and return them in the requested order.")]
    public IEnumerable<OrderOptions> OrderBy { get; set; }

    [Option('h', "cache-hours", Default = 24, HelpText = "The maximum age of a playlist cache in hours"
        + " before it is considered stale and the list of videos in it is refreshed."
        + " Note this doesn't apply to the videos themselves because their contents rarely change after upload."
        + $" Use '--{ClearCache.Command}' to clear videos associated with a playlist or channel if that's what you're after.")]
    public float CacheHours { get; set; }

    protected internal string ValidId { get; set; }
    protected abstract string KeyPrefix { get; }
    internal string StorageKey => KeyPrefix + ValidId;

    protected override string FormatInternal() => StorageKey;

    /// <summary>Mutually exclusive <see cref="OrderOptions"/>.</summary>
    internal static OrderOptions[] Orders = new[] { OrderOptions.uploaded, OrderOptions.score };

    /// <summary><see cref="Orders"/> and modifiers.</summary>
    public enum OrderOptions { uploaded, score, asc }
}

[Verb(Command, aliases: new[] { "playlist", "p" }, HelpText = "Searches the videos in a playlist.")]
internal sealed class SearchPlaylist : SearchPlaylistCommand
{
    internal const string Command = "search-playlist", StorageKeyPrefix = "playlist ";
    protected override string KeyPrefix => StorageKeyPrefix;

    [Value(0, MetaName = "playlist", Required = true, HelpText = "The playlist ID or URL.")]
    public string Playlist { get; set; }
}

[Verb("search-videos", aliases: new[] { "videos", "v" }, HelpText = "Searches the specified videos.")]
internal sealed class SearchVideos : SearchCommand
{
    internal const string QuoteIdsStartingWithDash = " Note that if the video ID starts with a dash, you have to quote it"
        + @" like ""-1a2b3c4d5e"" or use the entire URL to prevent it from being misinterpreted as a command option.";

    [Value(0, MetaName = "videos", Required = true,
        HelpText = "The space-separated YouTube video IDs and/or URLs." + QuoteIdsStartingWithDash)]
    public IEnumerable<string> Videos { get; set; }

    internal string[] ValidIds { get; set; }

    protected override string FormatInternal() => "videos " + ValidIds.Join(" ");
}

[Verb("open", aliases: new[] { "o" }, HelpText = "Opens app-related folders in a file browser.")]
internal sealed class Open
{
    [Value(0, MetaName = "folder", Required = true, HelpText = "The folder to open.")]
    public Folders Folder { get; set; }
}