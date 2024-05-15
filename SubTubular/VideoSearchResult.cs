using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

[Serializable]
[method: JsonConstructor]
public sealed class YoutubeSearchResult(string id, string title, string url, string thumbnail, string? channel = null)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Url { get; } = url;
    public string Thumbnail { get; } = thumbnail.StartsWith("http") ? thumbnail : "https:" + thumbnail;
    public string? Channel { get; } = channel;

    // for debugging
    public override string ToString() => Title;

    [Serializable]
    [method: JsonConstructor]
    public sealed class Cache(string search, YoutubeSearchResult[] results, DateTime created)
    {
        public string Search { get; set; } = search;
        public DateTime Created { get; set; } = created;
        public YoutubeSearchResult[] Results { get; set; } = results;
    }
}

public sealed class VideoSearchResult
{
    public required Video Video { get; set; }
    public MatchedText? TitleMatches { get; set; }
    public MatchedText? DescriptionMatches { get; set; }
    public MatchedText[]? KeywordMatches { get; set; }
    public CaptionTrackResult[]? MatchingCaptionTracks { get; set; }
    public double Score { get; internal set; }

    public sealed class CaptionTrackResult
    {
        public required CaptionTrack Track { get; set; }
        public required MatchedText Matches { get; set; }

        public Caption GetJoinendCaption(MatchedText matches)
        {
            var start = matches.Matches.Min(m => m.Start);
            var end = matches.Matches.Max(m => m.IncludedEnd);
            var captionAtFullTextIndex = Track.GetCaptionAtFullTextIndex();

            // find first and last captions containing parts of the padded match
            var first = captionAtFullTextIndex.Last(x => x.Key <= start);
            var last = captionAtFullTextIndex.Last(x => first.Key <= x.Key && x.Key <= end);

            var captions = captionAtFullTextIndex // span of captions containing the padded match
                .Where(x => first.Key <= x.Key && x.Key <= last.Key).ToArray();

            // return a single caption for all captions containing matches
            return new Caption
            {
                At = first.Value.At,
                Text = captions.Select(x => x.Value.Text)
                    .Where(text => text.IsNonWhiteSpace()) // skip included line breaks
                    .Select(text => text.NormalizeWhiteSpace(CaptionTrack.FullTextSeperator)) // replace included line breaks
                    .Join(CaptionTrack.FullTextSeperator)
            };
        }

        public (MatchedText synced, int captionStart) SyncWithCaptions(MatchedText matched, uint matchPadding)
        {
            // get (included) padded start and end index of matched Text
            var start = Math.Max(0, matched.Matches.Min(m => m.Start) - (int)matchPadding);
            var end = Math.Min(matched.Text.Length - 1, matched.Matches.Max(m => m.IncludedEnd) + (int)matchPadding);
            var captionAtFullTextIndex = Track.GetCaptionAtFullTextIndex();

            // find first and last captions containing parts of the padded match
            var first = captionAtFullTextIndex.Last(x => x.Key <= start);
            var last = captionAtFullTextIndex.Last(x => first.Key <= x.Key && x.Key <= end);

            var captions = captionAtFullTextIndex // span of captions containing the padded match
                .Where(x => first.Key <= x.Key && x.Key <= last.Key).ToArray();

            var text = captions.Select(x => x.Value.Text)
                .Where(text => text.IsNonWhiteSpace()) // skip included line breaks
                .Select(text => text.NormalizeWhiteSpace(CaptionTrack.FullTextSeperator)) // replace included line breaks
                .Join(CaptionTrack.FullTextSeperator);

            MatchedText synced = new(text, matched.Matches.Select(m =>
                new MatchedText.Match(m.Start - first.Key, m.Length)).ToArray());

            return (synced, first.Value.At);
        }

        public bool HasMatchesWithHours(uint matchPadding)
        {
            var fulltextStart = Math.Max(0, Matches.Matches.Max(m => m.Start) - matchPadding);
            var captionAtFullTextIndex = Track.GetCaptionAtFullTextIndex();
            var lastCaptionStart = captionAtFullTextIndex.Last(x => x.Key <= fulltextStart).Value;
            return lastCaptionStart.At > 3600; // sec per hour
        }
    }
}
