using System.Text.Json.Serialization;

namespace SubTubular;

using JP = JsonPropertyNameAttribute;

public sealed class Playlist
{
    public static ushort ShardSize = 200;
    private bool hasUnsavedChanges;

    [JP("t")] public required string Title { get; set; }
    [JP("u")] public required string ThumbnailUrl { get; set; }
    [JP("c")] public string? Channel { get; set; }
    [JP("l")] public DateTime Loaded { get; set; }

    [JsonInclude, JP("v")] private List<VideoInfo> videos = [];

    private VideoInfo? GetVideo(string videoId) => videos.SingleOrDefault(s => s.Id == videoId);

    /// <summary>The videos included in the <see cref="Playlist" /> (i.e. excluding dropped)
    /// ordered by <see cref="VideoInfo.PlaylistIndex"/>.</summary>
    public IOrderedEnumerable<VideoInfo> GetVideos()
    {
        changeToken?.Wait();
        try { return videos.Where(v => v.PlaylistIndex.HasValue).OrderBy(v => v.PlaylistIndex); }
        finally { changeToken?.Release(); }
    }

    internal uint GetVideoCount()
    {
        changeToken?.Wait();
        try { return (uint)videos.Count; }
        finally { changeToken?.Release(); }
    }

    // Retrieve all video IDs from all shards
    internal IEnumerable<string> GetVideoIds()
    {
        changeToken?.Wait();
        try { return videos.Select(v => v.Id); }
        finally { changeToken?.Release(); }
    }

    private SemaphoreSlim? changeToken; // ensures safe concurrent access during the update phase

    // creates the changeToken required for changes
    public IDisposable CreateChangeToken()
    {
        changeToken = new(1, 1);
        return changeToken;
    }

    /// <summary>Tries inserting or moving <paramref name="videoId"/>
    /// at or to <paramref name="newIndex"/> in <see cref="GetVideos()"/>
    /// and returns whether the operation resulted in any changes.</summary>
    public bool TryAddVideoId(string videoId, uint newIndex)
    {
        changeToken!.Wait();

        try
        {
            var video = GetVideo(videoId);

            if (video == null)
            {
                foreach (var dropped in videos.Where(v => newIndex == v.PlaylistIndex))
                    dropped.PlaylistIndex = null;

                video = new VideoInfo { Id = videoId, PlaylistIndex = newIndex };
                videos.Add(video);
                hasUnsavedChanges = true;
                return true;
            }
            else
            {
                if (newIndex == video.PlaylistIndex) return false;

                if (video.PlaylistIndex == null)
                    foreach (var dropped in videos.Where(v => newIndex == v.PlaylistIndex))
                        dropped.PlaylistIndex = null;
                else
                    foreach (var dropped in videos.Where(v => newIndex <= v.PlaylistIndex))
                        dropped.PlaylistIndex = null;

                video.PlaylistIndex = newIndex;

                hasUnsavedChanges = true;
                return true;
            }
        }
        finally { changeToken.Release(); }
    }

    internal bool SetUploaded(Video loadedVideo)
    {
        changeToken!.Wait();

        try
        {
            VideoInfo? video = GetVideo(loadedVideo.Id);

            // should not happen, just as a fall-back
            if (video == null)
            {
                video = new VideoInfo { Id = loadedVideo.Id };
                videos.Add(video);
                UpdateShardNumbers();
                hasUnsavedChanges = true;
            }

            bool madeChanges = false;

            if (video.Uploaded != loadedVideo.Uploaded)
            {
                video.Uploaded = loadedVideo.Uploaded;
                hasUnsavedChanges = true;
                madeChanges = true;
            }

            return madeChanges;
        }
        finally { changeToken.Release(); }
    }

    public void UpdateShardNumbers()
    {
        changeToken!.Wait();

        try
        {
            videos = videos.OrderBy(v => v.PlaylistIndex).ToList();
            var firstLoaded = videos.Find(v => v.ShardNumber == 0);
            var indexOfFirstLoaded = firstLoaded == null ? 0 : videos.IndexOf(firstLoaded);

            foreach (var video in videos.Where(v => v.ShardNumber == null))
            {
                var index = videos.IndexOf(video);
                int translatedIndex = index - indexOfFirstLoaded;
                var shardNumber = (short?)(translatedIndex < 0 ? ((translatedIndex + 1) / ShardSize) - 1 : translatedIndex / ShardSize);

                if (video.ShardNumber != shardNumber)
                {
                    video.ShardNumber = shardNumber;
                    hasUnsavedChanges = true;
                }
            }
        }
        finally { changeToken.Release(); }
    }

    internal void UpdateLoaded()
    {
        changeToken!.Wait();
        Loaded = DateTime.UtcNow;
        hasUnsavedChanges = true;
        changeToken.Release();
    }

    internal async ValueTask SaveAsync(Func<Task> save)
    {
        if (!hasUnsavedChanges) return;
        await changeToken!.WaitAsync();

        try
        {
            await save();
            hasUnsavedChanges = false;
        }
        finally
        {
            changeToken.Release();
        }
    }

    public sealed class VideoInfo
    {
        [JP("i")] public required string Id { get; set; }
        [JP("s")] public short? ShardNumber { get; set; }
        [JP("u")] public DateTime? Uploaded { get; set; }

        //set if included, null if dropped
        [JP("n")] public uint? PlaylistIndex { get; set; }

        // used for debugging
        public override string ToString()
        {
            var shardNumber = ShardNumber.HasValue ? " s" + ShardNumber : null;
            var playlistIndex = PlaylistIndex.HasValue ? " n" + PlaylistIndex : null;
            var uploaded = Uploaded.HasValue ? $" {Uploaded:d}" : null;
            return Id + playlistIndex + shardNumber + uploaded;
        }
    }
}
