using System;
using System.Collections.Generic;
using System.Linq;

namespace SubTubular
{
    [Serializable]
    public sealed class Playlist
    {
        public DateTime Loaded { get; set; }
        public IList<string> VideoIds { get; set; } = new List<string>();
    }

    [Serializable]
    public sealed class Video
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string[] Keywords { get; set; }

        /// <summary>Upload time in UTC.</summary>
        public DateTime Uploaded { get; set; }

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

        public CaptionTrack() { } //required by serializer

        /// <summary>Use this to clone a Captiontrack to include in a VideoSearchResult.
        /// Captions will be set to matchingCaptions instead of cloning track.Captions.</summary>
        /// <param name="track">The track to clone.</param>
        /// <param name="matchingCaptions">The matching captions.</param>
        internal CaptionTrack(CaptionTrack track, List<Caption> matchingCaptions)
        {
            LanguageName = track.LanguageName;
            Captions = matchingCaptions;
        }

        #region FullText
        internal const string FullTextSeperator = " ";
        private string fullText;
        private Dictionary<Caption, int> captionAtFullTextIndex;

        // aggregates captions into fullText to enable matching phrases across caption boundaries
        internal string FullText
        {
            get
            {
                if (fullText == null) CacheFullText();
                return fullText;
            }
        }

        internal Dictionary<Caption, int> CaptionAtFullTextIndex
        {
            get
            {
                if (captionAtFullTextIndex == null) CacheFullText();
                return captionAtFullTextIndex;
            }
        }

        private void CacheFullText()
        {
            captionAtFullTextIndex = new Dictionary<Caption, int>();

            fullText = Captions
                .Where(caption => !string.IsNullOrWhiteSpace(caption.Text)) // skip included line breaks
                .Aggregate(string.Empty, (fullText, caption) =>
            {
                var isFirst = fullText.Length == 0;

                // remember at what index in the fullText the caption starts
                captionAtFullTextIndex.Add(caption, isFirst ? 0 : fullText.Length + FullTextSeperator.Length);

                var normalized = caption.Text.NormalizeWhiteSpace(FullTextSeperator); // replace included line breaks
                return isFirst ? normalized : fullText + FullTextSeperator + normalized;
            });
        }
        #endregion
    }

    [Serializable]
    public sealed class Caption
    {
        /// <summary>The offset from the start of the video in seconds.</summary>
        public int At { get; set; }

        public string Text { get; set; }

        // for comparing captions when finding them in a caption track
        public override bool Equals(object obj) => obj == null ? false : obj.GetHashCode() == GetHashCode();
        public override int GetHashCode() => HashCode.Combine(At, Text);
    }
}