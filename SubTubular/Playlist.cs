using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

using CaptionTrackDownloadStatus = (Playlist.VideoInfo.CaptionStatus? status, Playlist.VideoInfo[] videos);
using JP = JsonPropertyNameAttribute;

public sealed class Playlist
{
    /*  Non-constant fields should not be visible
     *  This is a user preference that should be kept public and writable
     *  to enable informed configuration at app start-up. */
#pragma warning disable CA2211
    /// <summary>The number of <see cref="Video"/>s to index in one <see cref="VideoIndex"/> shard.
    /// Use this to balance how often you hit the disk for the next shard index vs. how much memory you need to load it.
    /// Configure this once when starting the app - changing this during runtime is not thread-safe.
    /// When changing this in between app usages, delete the full-text indexes and caches of playlists and channels
    /// to avoid inefficient bloat and duplication in the indexes. They'll be re-created to match the new shard size efficiently.</summary>
    public static ushort ShardSize = 200;
#pragma warning restore CA2211

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

    public CaptionTrackDownloadStatus[] GetCaptionTrackDownloadStates()
        => GetVideos()
            .GroupBy(v => v.CaptionTrackDownloadStatus)
            .Select(g => (g.Key, g.ToArray())).ToArray();

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

                video = new VideoInfo { Id = videoId, PlaylistIndex = newIndex, CaptionTrackDownloadStatus = VideoInfo.CaptionStatus.UnChecked };
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

    internal bool Update(Video loadedVideo)
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

            VideoInfo.CaptionStatus? captionStatus = loadedVideo.CaptionTracks == null ? VideoInfo.CaptionStatus.UnChecked
                : loadedVideo.CaptionTracks.Count == 0 ? VideoInfo.CaptionStatus.None
                : loadedVideo.CaptionTracks.WithErrors().Any() ? VideoInfo.CaptionStatus.Error
                : null;

            if (video.Uploaded != loadedVideo.Uploaded
                || video.CaptionTrackDownloadStatus != captionStatus
                || video.Keywords == null || !loadedVideo.Keywords.ToHashSet().SetEquals(video.Keywords))
            {
                video.Uploaded = loadedVideo.Uploaded;
                video.CaptionTrackDownloadStatus = captionStatus;
                video.Keywords = loadedVideo.Keywords;
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

    // required to enable structurally comparing PlaylistGroup
    public override bool Equals(object? other) => other != null && other is Playlist pl && ThumbnailUrl == pl.ThumbnailUrl;
    public override int GetHashCode() => ThumbnailUrl.GetHashCode(); // because Equals is overridden

    public sealed class VideoInfo
    {
        [JP("i")] public required string Id { get; set; }
        [JP("s")] public short? ShardNumber { get; set; }
        [JP("u")] public DateTime? Uploaded { get; set; }

        //set if included, null if dropped
        [JP("n")] public uint? PlaylistIndex { get; set; }

        [JP("k")] public string[]? Keywords { get; set; }

        /// <summary>Empty if download succeeded for all.</summary>
        [JP("c")] public CaptionStatus? CaptionTrackDownloadStatus { get; set; }

        // used for debugging
        public override string ToString()
        {
            var shardNumber = ShardNumber.HasValue ? " s" + ShardNumber : null;
            var playlistIndex = PlaylistIndex.HasValue ? " n" + PlaylistIndex : null;
            var uploaded = Uploaded.HasValue ? $" {Uploaded:d}" : null;
            return Id + playlistIndex + shardNumber + uploaded;
        }

        public enum CaptionStatus { UnChecked, None, Error }
    }
}

public static class PlaylistExtensions
{
    internal static bool IsComplete(this Playlist.VideoInfo.CaptionStatus? status) => status == null || status == Playlist.VideoInfo.CaptionStatus.None;

    public static CommandScope.Notification[] AsNotifications(this CaptionTrackDownloadStatus[] states, Func<CaptionTrackDownloadStatus, bool>? predicate = null)
        => states
            .Where(s => predicate == null || predicate(s))
            .Select(s =>
            {
                var issue = s.status switch
                {
                    null => " all caption tracks dowloaded",
                    Playlist.VideoInfo.CaptionStatus.None => "out caption tracks",
                    Playlist.VideoInfo.CaptionStatus.Error => " errors during caption track download",
                    Playlist.VideoInfo.CaptionStatus.UnChecked => " unchecked caption track status",
                    _ => " unknown caption track status"
                };

                return new CommandScope.Notification($"{s.videos.Length} videos with{issue}");
            })
            .ToArray();

    public static Dictionary<PlaylistLikeScope, CaptionTrackDownloadStatus[]> GetCaptionTrackDownloadStatus(this OutputCommand command)
        => command.GetPlaylistLikeScopes().ToDictionary(scope => scope, scope => scope.SingleValidated.Playlist!.GetCaptionTrackDownloadStates());
}
