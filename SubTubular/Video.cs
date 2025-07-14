using System.Text;
using SubTubular.Extensions;

namespace SubTubular;

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
    /// This is a work-around for <see cref="CacheManager"/> not cleaning up playlist indexes when singular videos are cleared.</summary>
    internal bool UnIndexed { get; set; }

    /// <summary>Null if tracks have not been downloaded.</summary>
    public IList<CaptionTrack>? CaptionTracks { get; set; }

    internal static string GuessThumbnailUrl(string videoId) => $"https://img.youtube.com/vi/{videoId}/default.jpg";
}

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
    private readonly Lock accessToken = new(); // for thread-safe access
    private Timer? dropCacheTimer;

    // aggregates captions into fullText to enable matching phrases across caption boundaries
    internal string? GetFullText()
    {
        lock (accessToken)
        {
            if (fullText == null && Captions != null) CacheFullText();
            RescheduleCacheDrop();
            return fullText;
        }
    }

    internal Dictionary<int, Caption>? GetCaptionAtFullTextIndex()
    {
        lock (accessToken)
        {
            if (captionAtFullTextIndex == null && Captions != null) CacheFullText();
            RescheduleCacheDrop();
            return captionAtFullTextIndex;
        }
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

    private void RescheduleCacheDrop()
    {
        dropCacheTimer?.Dispose(); // Dispose of any existing timer
        dropCacheTimer = new Timer(DropCache, null, 1000, Timeout.Infinite);
    }

    private void DropCache(object? state)
    {
        lock (accessToken)
        {
            fullText = null;
            captionAtFullTextIndex = null;

            // Dispose of the timer and set it to null
            dropCacheTimer?.Dispose();
            dropCacheTimer = null;
        }
    }
    #endregion
}

public sealed class Caption
{
    /// <summary>The offset from the start of the video in seconds.</summary>
    public int At { get; set; }

    public required string Text { get; set; }

    // for comparing captions when finding them in a caption track
    public override int GetHashCode() => HashCode.Combine(At, Text);
}
