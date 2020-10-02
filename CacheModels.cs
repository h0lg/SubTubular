using System;
using System.Collections.Generic;

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
        public Caption[] Captions { get; set; }

        public CaptionTrack() { } //required by serializer

        /// <summary>Use this to clone a Captiontrack to include in a VideoSerachResult.
        /// Captions will be set to matchingCaptions instead of cloning track.Captions.</summary>
        /// <param name="track">The track to clone.</param>
        /// <param name="matchingCaptions">The matching captions.</param>
        internal CaptionTrack(CaptionTrack track, Caption[] matchingCaptions)
        {
            this.LanguageName = track.LanguageName;
            this.Captions = matchingCaptions;
        }
    }

    [Serializable]
    public sealed class Caption
    {
        /// <summary>The offset from the start of the video in seconds.</summary>
        public int At { get; set; }

        public string Text { get; set; }
    }
}