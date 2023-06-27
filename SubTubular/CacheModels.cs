using System.Text;

namespace SubTubular;

[Serializable]
public sealed class Playlist
{
    public DateTime Loaded { get; set; }

    /// <summary>The <see cref="Video.Id"/>s and (optional) upload dates
    /// of the videos included in the <see cref="Playlist" />.</summary>
    public IDictionary<string, DateTime?> Videos { get; set; } = new Dictionary<string, DateTime?>();
}

[Serializable]
public sealed class Video
{
    internal const string StorageKeyPrefix = "video ";

    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string[] Keywords { get; set; }

    /// <summary>Upload time in UTC.</summary>
    public DateTime Uploaded { get; set; }

    /// <summary>Set internally and temporarily when a video was re-loaded from YouTube and needs re-indexing.
    /// This is a work-around for <see cref="ClearCache"/> not cleaning up playlist indexes when singular videos are cleared.</summary>
    internal bool UnIndexed { get; set; }

    public IList<CaptionTrack> CaptionTracks { get; set; } = new List<CaptionTrack>();
}

[Serializable]
public sealed class CaptionTrack
{
    public string LanguageName { get; set; }
    public string Url { get; set; }
    public List<Caption> Captions { get; set; }
    public string Error { get; set; }
    public string ErrorMessage { get; set; }

    #region FullText
    internal const string FullTextSeperator = " ";
    private string fullText;
    private Dictionary<int, Caption> captionAtFullTextIndex;

    // aggregates captions into fullText to enable matching phrases across caption boundaries
    internal string GetFullText()
    {
        if (fullText == null) CacheFullText();
        return fullText;
    }

    internal Dictionary<int, Caption> GetCaptionAtFullTextIndex()
    {
        if (captionAtFullTextIndex == null) CacheFullText();
        return captionAtFullTextIndex;
    }

    private void CacheFullText()
    {
        var writer = new StringBuilder();
        var captionsAtFullTextIndex = new Dictionary<int, Caption>();

        foreach (var caption in Captions)
        {
            if (string.IsNullOrWhiteSpace(caption.Text)) continue; // skip included line breaks
            var isFirst = writer.Length == 0;
            captionsAtFullTextIndex[isFirst ? 0 : writer.Length + FullTextSeperator.Length] = caption;
            var normalized = caption.Text.NormalizeWhiteSpace(FullTextSeperator); // replace included line breaks
            writer.Append(isFirst ? normalized : FullTextSeperator + normalized);
        }

        captionAtFullTextIndex = captionsAtFullTextIndex;
        fullText = writer.ToString();
    }
    #endregion

    #region Indexing
    /// <summary>The (constant) suffix of <see cref="FieldName"/>.</summary>
    internal const string FieldSuffix = "Caps";

    private string fieldName;

    /// <summary>For indexing <see cref="CaptionTrack"/>s with a <see cref="Video"/>
    /// as dynamic fields identifyable by <see cref="LanguageName"/>.</summary>
    internal string FieldName => fieldName ??= LanguageName
        .Replace(" (auto-generated)", "Auto") // to shorten field name
        /* upper-case first letters to preserve word boundary and remove chars preventing use in field queries */
        .UpperCaseFirstLetters().ReplaceNonWordCharacters() + FieldSuffix;
    #endregion
}

[Serializable]
public sealed class Caption
{
    /// <summary>The offset from the start of the video in seconds.</summary>
    public int At { get; set; }

    public string Text { get; set; }

    // for comparing captions when finding them in a caption track
    public override int GetHashCode() => HashCode.Combine(At, Text);
}

/// <summary>Maps valid channel aliases by <see cref="Type"/> and <see cref="Value"/>
/// to an accessible <see cref="ChannelId"/> or null if none was found.</summary>
[Serializable]
public sealed class ChannelAliasMap
{
    internal const string StorageKey = "known channel alias maps";

    public string Type { get; set; }
    public string Value { get; set; }
    public string ChannelId { get; set; }

    internal static (string, string) GetTypeAndValue(object alias) => (alias.GetType().Name, alias.ToString());

    internal static Task<List<ChannelAliasMap>> LoadList(DataStore dataStore)
        => dataStore.GetAsync<List<ChannelAliasMap>>(StorageKey);

    internal static Task SaveList(List<ChannelAliasMap> maps, DataStore dataStore)
        => dataStore.SetAsync(StorageKey, maps);
}