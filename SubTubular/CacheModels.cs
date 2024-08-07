using System.Collections.Concurrent;
using System.Text;
using SubTubular.Extensions;

namespace SubTubular;

[Serializable]
public sealed class Playlist
{
    public required string Title { get; set; }
    public required string ThumbnailUrl { get; set; }
    public string? Channel { get; set; }
    public DateTime Loaded { get; set; }

    /// <summary>The <see cref="Video.Id"/>s and (optional) upload dates
    /// of the videos included in the <see cref="Playlist" />.</summary>
    public IDictionary<string, DateTime?> Videos { get; set; } = new Dictionary<string, DateTime?>();

    internal IEnumerable<string> GetVideoIds() => Videos.Keys;
    internal DateTime? GetVideoUploaded(string id) => Videos.TryGetValue(id, out var uploaded) ? uploaded : null;

    internal void AddVideoIds(string[] freshKeys)
    {
        // use new order but append older entries; note that this leaves remotely deleted videos in the playlist
        Videos = freshKeys.Concat(GetVideoIds().Except(freshKeys)).ToDictionary(id => id, GetVideoUploaded);
    }

    internal bool SetUploaded(Video video)
    {
        if (Videos[video.Id] != video.Uploaded)
        {
            Videos[video.Id] = video.Uploaded;
            return true;
        }

        return false;
    }
}

[Serializable]
public sealed class Video
{
    public const string StorageKeyPrefix = "video ";

    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Channel { get; set; }
    public required string Thumbnail { get; set; }
    public required string[] Keywords { get; set; }

    /// <summary>Upload time in UTC.</summary>
    public DateTime Uploaded { get; set; }

    /// <summary>Set internally and temporarily when a video was re-loaded from YouTube and needs re-indexing.
    /// This is a work-around for <see cref="CacheClearer"/> not cleaning up playlist indexes when singular videos are cleared.</summary>
    internal bool UnIndexed { get; set; }

    public IList<CaptionTrack> CaptionTracks { get; set; } = [];
}

[Serializable]
public sealed class CaptionTrack
{
    public required string LanguageName { get; set; }
    public required string Url { get; set; }
    public List<Caption>? Captions { get; set; }
    public string? Error { get; set; }
    public string? ErrorMessage { get; set; }

    #region FullText
    internal const string FullTextSeperator = " ";
    private string? fullText;
    private Dictionary<int, Caption>? captionAtFullTextIndex;

    // aggregates captions into fullText to enable matching phrases across caption boundaries
    internal string GetFullText()
    {
        if (fullText == null) CacheFullText();
        return fullText!;
    }

    internal Dictionary<int, Caption> GetCaptionAtFullTextIndex()
    {
        if (captionAtFullTextIndex == null) CacheFullText();
        return captionAtFullTextIndex!;
    }

    private void CacheFullText()
    {
        if (Captions == null) throw new InvalidOperationException(
            "You may only call this method on instances with " + nameof(Captions));

        var writer = new StringBuilder();
        var captionsAtFullTextIndex = new Dictionary<int, Caption>();

        foreach (var caption in Captions)
        {
            if (caption.Text.IsNullOrWhiteSpace()) continue; // skip included line breaks
            var isFirst = writer.Length == 0;
            captionsAtFullTextIndex[isFirst ? 0 : writer.Length + FullTextSeperator.Length] = caption;
            var normalized = caption.Text.NormalizeWhiteSpace(FullTextSeperator); // replace included line breaks
            writer.Append(isFirst ? normalized : FullTextSeperator + normalized);
        }

        captionAtFullTextIndex = captionsAtFullTextIndex;
        fullText = writer.ToString();
    }
    #endregion
}

[Serializable]
public sealed class Caption
{
    /// <summary>The offset from the start of the video in seconds.</summary>
    public int At { get; set; }

    public required string Text { get; set; }

    // for comparing captions when finding them in a caption track
    public override int GetHashCode() => HashCode.Combine(At, Text);
}

/// <summary>Maps valid channel aliases by <see cref="Type"/> and <see cref="Value"/>
/// to an accessible <see cref="ChannelId"/> or null if none was found.</summary>
[Serializable]
public sealed class ChannelAliasMap
{
    internal const string StorageKey = "known channel alias maps";

    public required string Type { get; set; }
    public required string Value { get; set; }
    public string? ChannelId { get; set; }

    internal static (string, string) GetTypeAndValue(object alias) => (alias.GetType().Name, alias.ToString()!);

    #region STORAGE
    private static readonly ConcurrentDictionary<(string Type, string Value), ChannelAliasMap> localCache = new(); // for less file I/O during concurrent validations
    private static readonly TimeSpan inactivityPeriod = TimeSpan.FromSeconds(5); // inactivity period before saving changes and/or discarding local cache
    private static Timer? inactivityTimer; // helps scheduling the persistence of changes and clearing of cache
    private static bool changesMade = false; // tracks if changes were made to the local cache
    private static readonly SemaphoreSlim access = new(1, 1); // ensures thread-safe access and updates to local cache and dataStore version

    public override int GetHashCode() => HashCode.Combine(Type, Value); // for safely using HashSet

    /// <summary>Loads the current <see cref="ChannelAliasMap"/>s from the <see cref="localCache"/>
    /// or the <paramref name="dataStore"/>.</summary>
    internal static async Task<HashSet<ChannelAliasMap>> LoadListAsync(DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            DebounceClearCache(dataStore); // to prevent clearing local cache during a longer running validation

            if (localCache.IsEmpty)
            {
                // load data from the data store if the local cache is empty
                var stored = await dataStore.GetAsync<HashSet<ChannelAliasMap>>(StorageKey) ?? [];

                foreach (var entry in stored)
                {
                    localCache[(entry.Type, entry.Value)] = entry;
                }
            }

            return localCache.Values.ToHashSet();
        }
        finally
        {
            access.Release();
        }
    }

    /// <summary>Adds <paramref name="entries"/> to the <see cref="localCache"/>
    /// and saves them via <see cref="DebounceClearCache(DataStore)"/> to the <paramref name="dataStore"/>.</summary>
    internal static async Task AddEntriesAsync(IEnumerable<ChannelAliasMap> entries, DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            DebounceClearCache(dataStore);

            foreach (var entry in entries)
            {
                var key = (entry.Type, entry.Value);
                if (localCache.ContainsKey(key)) continue;
                localCache[key] = entry;
                changesMade = true; // only if entry was added
            }
        }
        finally
        {
            access.Release();
        }
    }

    /// <summary>Removes <paramref name="entries"/> from the <see cref="localCache"/>
    /// and saves changes via <see cref="DebounceClearCache(DataStore)"/> to the <paramref name="dataStore"/>.</summary>
    internal static async Task RemoveEntriesAsync(IEnumerable<ChannelAliasMap> entries, DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            DebounceClearCache(dataStore);

            foreach (var entry in entries)
            {
                if (localCache.TryRemove((entry.Type, entry.Value), out _))
                    changesMade = true; // Mark changes as made if an entry was actually removed
            }
        }
        finally
        {
            access.Release();
        }
    }

    /// <summary>Resets the <see cref="inactivityTimer"/> and schedules <see cref="ClearCacheAsync(DataStore)"/>
    /// to the <paramref name="dataStore"/> for when the <see cref="inactivityPeriod"/> expires.</summary>
    private static void DebounceClearCache(DataStore dataStore)
    {
        if (inactivityTimer == null)
            inactivityTimer = new Timer(async _ => await ClearCacheAsync(dataStore), null, inactivityPeriod, Timeout.InfiniteTimeSpan);
        else inactivityTimer.Change(inactivityPeriod, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Clears the <see cref="localCache"/>, saving changes
    /// to the <paramref name="dataStore"/> before if <see cref="changesMade"/>.</summary>
    private static async Task ClearCacheAsync(DataStore dataStore)
    {
        await access.WaitAsync();

        try
        {
            if (changesMade)
            {
                // Save the local cache to the data store
                await dataStore.SetAsync(StorageKey, localCache.Values.ToHashSet());

                changesMade = false; // Reset changes flag after saving
            }

            // Clear the local cache to free up memory
            localCache.Clear();

            // Stop the inactivity timer after saving
            inactivityTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        finally
        {
            access.Release();
        }
    }
    #endregion
}