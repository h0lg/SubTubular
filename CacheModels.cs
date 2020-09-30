using System;
using System.Collections.Generic;
using System.Linq;

namespace SubTubular
{
    [Serializable]
    public sealed class Playlist
    {
        public DateTime Loaded { get; set; }
        public IList<Video> Videos { get; set; } = new List<Video>();
    }

    [Serializable]
    public sealed class Video
    {
        public string Id { get; set; }

        /// <summary>Upload time in UTC.</summary>
        public DateTime Uploaded { get; set; }
    }

    [Serializable]
    public sealed class CaptionTrack
    {
        public Caption[] Captions { get; set; }

        internal Caption[] FindCaptions(IEnumerable<string> terms) => Captions
            .Where(c => terms.Any(t => c.Text.Contains(t, StringComparison.InvariantCultureIgnoreCase)))
            .ToArray();
    }

    [Serializable]
    public sealed class Caption
    {
        /// <summary>The offset from the start of the video in seconds.</summary>
        public int At { get; set; }

        public string Text { get; set; }
    }
}