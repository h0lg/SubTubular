namespace SubTubular;

public readonly struct LastAccessGroup(string timeSpanLabel,
    FileInfo[] searches, FileInfo[] playlistLikes, FileInfo[] indexes, FileInfo[] videos, FileInfo[] thumbnails)
{
    public string TimeSpanLabel { get; } = timeSpanLabel;
    public FileInfo[] Searches { get; } = searches;
    public FileInfo[] PlaylistLikes { get; } = playlistLikes;
    public FileInfo[] Indexes { get; } = indexes;
    public FileInfo[] Videos { get; } = videos;
    public FileInfo[] Thumbnails { get; } = thumbnails;

    public IEnumerable<FileInfo> GetFiles()
    {
        foreach (var s in Searches) yield return s;
        foreach (var p in PlaylistLikes) yield return p;
        foreach (var i in Indexes) yield return i;
        foreach (var v in Videos) yield return v;
        foreach (var t in Thumbnails) yield return t;
    }

    public LastAccessGroup Remove(FileInfo[] files)
        => new(TimeSpanLabel, [.. Searches.Except(files)],
            [.. PlaylistLikes.Except(files)], [.. Indexes.Except(files)],
            [.. Videos.Except(files)], [.. Thumbnails.Except(files)]);
}

static partial class CacheManager
{
    public static LastAccessGroup[] LoadByLastAccess(string cacheFolder)
    {
        var now = DateTime.Now;

        return [.. FileHelper
            .EnumerateFiles(cacheFolder, "*", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastAccessTime) // latest first
            .GroupBy(f => DescribeTimeSpan(now - f.LastAccessTime))
            .Select(group =>
            {
                var searches = group.ToArray().GetSearches().GetFiles().ToArray();
                var json = group.WithExtension(JsonFileDataStore.FileExtension).Except(searches).ToArray();
                var videos = json.WithPrefix(Video.StorageKeyPrefix).ToArray();
                var playlistLikes = json.Except(videos).ToArray();
                var indexes = group.WithExtension(VideoIndexRepository.FileExtension).ToArray();
                var thumbnails = group.WithExtension(string.Empty).ToArray();

                return new LastAccessGroup(group.Key, searches: searches,
                    playlistLikes: playlistLikes, indexes: indexes, videos: videos, thumbnails: thumbnails);
            })];
    }

    public static IEnumerable<LastAccessGroup> Remove(this IEnumerable<LastAccessGroup> groups, FileInfo[] files)
        => groups.Select(g => g.Remove(files)).Where(g => g.GetFiles().Any());

    // Function to describe a TimeSpan into specific ranges
    private static string DescribeTimeSpan(TimeSpan ts)
    {
        if (ts < TimeSpan.FromDays(1.0)) return "day";
        if (ts < TimeSpan.FromDays(7.0)) return $"{ts.Days + 1} days";

        if (ts < TimeSpan.FromDays(30.0))
        {
            var weeks = ts.Days / 7;
            return weeks == 0 ? "week" : $"{weeks + 1} weeks";
        }

        if (ts < TimeSpan.FromDays(90)) // 90 days for roughly 3 months (quarter year)
        {
            var months = ts.Days / 30;
            return months == 0 ? "month" : $"{months + 1} months";
        }

        return "eon";
    }
}
