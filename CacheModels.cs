using System;

namespace SubTubular
{
    [Serializable]
    public sealed class Playlist
    {
        public DateTime Loaded { get; set; }
        public Video[] Videos { get; set; }
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
    }

    [Serializable]
    public sealed class Caption
    {
        /// <summary>The offset from the start of the video in seconds.</summary>
        public int At { get; set; }

        public string Text { get; set; }
    }
}