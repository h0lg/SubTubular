using CommandLine;

namespace SubTubular;

[Verb("search-channel", aliases: new[] { "channel", "c" },
    HelpText = "Searches the videos in a channel's Uploads playlist."
    + $" This is a glorified '{SearchPlaylist.Command}'.")]
internal sealed class SearchChannel : SearchPlaylistCommand
{
    internal const string StorageKeyPrefix = "channel ";

    [Value(0, MetaName = "channel", Required = true,
        HelpText = "The channel ID, handle, slug, user name or a URL for either of those.")]
    public string Alias { get; set; }

    protected override string KeyPrefix => StorageKeyPrefix;
    internal object[] ValidAliases { get; set; }
}