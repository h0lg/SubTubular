using System;
using System.Collections.Generic;
using System.Text;

namespace SubTubular
{
    [Serializable]
    public sealed class Playlist
    {
        public DateTime Loaded { get; set; }

        /// <summary>The IDs and (optional) upload dates of the videos included in the <see cref="Playlist" /></summary>
        /// <typeparam name="string">The video ID.</typeparam>
        /// <typeparam name="DateTime?">The upload date of the video.</typeparam>
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

        #region INDEXING
        /// <summary>Used for separating <see cref="VideoId"/> from <see cref="LanguageName"/> in <see cref="Key"/>.</summary>
        internal const char MultiPartKeySeparator = '#';

        /// <summary>The <see cref="Video.Id"/>. Needs to be set before indexing to generate a valid <see cref="Key"/>.</summary>
        internal string VideoId { private get; set; }

        /// <summary>Used for indexing. Conatins <see cref="VideoId"/> and <see cref="LanguageName"/>
        /// separated by <see cref="MultiPartKeySeparator"/> to identify the matched video and caption track.</summary>
        internal string Key => VideoId + MultiPartKeySeparator + LanguageName;
        #endregion

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
}