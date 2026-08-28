using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

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

    private bool mayChange, hasUnsavedChanges;

    [JP("t")] public required string Title { get; set; }
    [JP("u")] public required string ThumbnailUrl { get; set; }
    [JP("c")] public string? Channel { get; set; }
    [JP("l")] public DateTime Loaded { get; set; }
    [JP("n")] public int? Count { get; internal set; }

    [JsonInclude, JP("v")] private List<VideoInfo> videos = [];

    private VideoInfo? GetVideo(string videoId) => videos.SingleOrDefault(s => s.Id == videoId);

    /// <summary>The videos included in the <see cref="Playlist" /> (i.e. excluding dropped)
    /// ordered by <see cref="VideoInfo.PlaylistIndex"/>.</summary>
    public async Task<IReadOnlyList<VideoInfo>> GetVideosAsync()
        => await RunSync(() => videos.Where(v => v.PlaylistIndex.HasValue).OrderBy(v => v.PlaylistIndex).ToArray()).ContinueAnywhere(); // execute LINQ inside lock

    private async ValueTask<T> RunSync<T>(Func<T> action)
    {
        if (changeToken != null)
            await changeToken.WaitAsync().ContinueAnywhere();

        try { return action(); }
        finally { changeToken?.Release(); }
    }

    private async ValueTask SyncedAction(Action action)
    {
        if (changeToken != null)
            await changeToken.WaitAsync().ContinueAnywhere();

        try { action(); }
        finally { changeToken?.Release(); }
    }

    // Retrieve all video IDs from all shards
    internal async Task<IReadOnlyList<string>> GetVideoIdsAsync()
        => await RunSync(() => videos.Ids().ToArray()).ContinueAnywhere(); // execute LINQ inside lock

    /// <summary>Ensures safe concurrent access to the playlist during the update phase.
    /// Only needs to be set via <see cref="CreateChangeToken(Func{Task})"/>
    /// when starting a process that makes writing changes to the playlist.</summary>
    private SemaphoreSlim? changeToken;

    // creates the changeToken required for making changes
    public IAsyncDisposable CreateChangeToken(Func<Task> savePlaylist)
    {
        if (mayChange) throw new InvalidOperationException("A change scope is already active.");
        mayChange = true;
        changeToken ??= new(1, 1);
        return new ChangeTokenResetter(this, savePlaylist); // resets mayChange to false when disposed
    }

    /// <summary>Tries inserting or moving <paramref name="videoId"/>
    /// at or to <paramref name="newIndex"/> in <see cref="GetVideosAsync()"/>
    /// and returns whether the operation resulted in any changes.</summary>
    public async Task<bool> TryAddVideoIdAsync(string videoId, uint newIndex)
    {
        if (!mayChange) return false; // made no changes

        return await RunSync(() =>
        {
            var video = GetVideo(videoId);

            if (video == null)
            {
                DropVideos(v => v.PlaylistIndex == newIndex); // drop videos occupying the new video's index

                video = new VideoInfo
                {
                    Id = videoId,
                    PlaylistIndex = newIndex,
                    CaptionTrackDownloadStatus = CommandScope.CaptionStatus.UnChecked
                };

                videos.Add(video);
                hasUnsavedChanges = true;
                return true; // made changes
            }
            else // video exists, update if necessary
            {
                if (newIndex == video.PlaylistIndex) return false; // nothing to do, made no changes

                if (video.PlaylistIndex == null) // existing video was dropped before
                    DropVideos(v => v.PlaylistIndex == newIndex); // drop videos occupying the new index
                else // drop all videos with higher index than the new one; they all need re-indexing
                    DropVideos(v => newIndex <= v.PlaylistIndex);

                video.PlaylistIndex = newIndex;
                hasUnsavedChanges = true;
                return true; // made changes
            }
        }).ContinueAnywhere();
    }

    internal async Task<bool> UpdateAsync(Video loadedVideo)
    {
        if (!mayChange) return false; // no change token, no changes

        return await RunSync(() =>
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
            CommandScope.CaptionStatus? captionStatus = loadedVideo.GetCaptionTrackDownloadStatus();

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
        }).ContinueAnywhere();
    }

    internal event Action? ShardNumbersUpdated;

    public async Task UpdateShardNumbersAsync()
    {
        if (!mayChange) return; // make no changes without change token
        await SyncedAction(UpdateShardNumbers).ContinueAnywhere();
    }

    private void UpdateShardNumbers()
    {
        var withoutShardNumber = videos.Where(v => v.ShardNumber == null).ToArray();
        if (withoutShardNumber.Length == 0) return;

        videos = [.. videos.OrderBy(v => v.PlaylistIndex)];
        int firstLoadedIndex = GetIndexOfFirstLoadedVideoUnsynced();

        foreach (var video in withoutShardNumber)
        {
            short? shardNumber = CalculateShardNumber(videos.IndexOf(video), firstLoadedIndex);

            if (video.ShardNumber != shardNumber)
            {
                video.ShardNumber = shardNumber;
                hasUnsavedChanges = true;
            }
        }

        ShardNumbersUpdated?.Invoke();
    }

    /// <summary>Calculates the shard number for the video with <see cref="VideoInfo.PlaylistIndex"/> at <paramref name="index"/>
    /// deterministically for the configured <see cref="ShardSize"/>
    /// using an index relative to the <paramref name="firstLoadedVideoIndex"/> -
    /// so that already loaded videos stay in the same shard when videos are added to the top/front of the playlist
    /// (as is normal for the Uploads playlist of a channel) as well as to its end/bottom.
    /// This keeps the shards fairly stable over time
    /// by preventing videos from "moving" through the shards when a playlist is prepended to,
    /// which would have the effect of accumulating stale data about videos in the top/front index shards -
    /// from videos that once were indexed in that shard but have since pushed into another shard.</summary>
    /// <param name="index">The index of the video to calculate the shard number for.</param>
    /// <param name="firstLoadedVideoIndex">The index of the first loaded video, determined via <see cref="GetIndexOfFirstLoadedVideoAsync"/>.</param>
    internal static short? CalculateShardNumber(int index, int firstLoadedVideoIndex)
    {
        int translatedIndex = index - firstLoadedVideoIndex;
        return (short?)(translatedIndex < 0 ? ((translatedIndex + 1) / ShardSize) - 1 : translatedIndex / ShardSize);
    }

    /// <summary>Figures out the index of the first loaded video, which was indexed in shard 0.</summary>
    internal ValueTask<int> GetIndexOfFirstLoadedVideoAsync() => RunSync(GetIndexOfFirstLoadedVideoUnsynced);

    private int GetIndexOfFirstLoadedVideoUnsynced()
    {
        var firstLoaded = videos.Find(v => v.ShardNumber == 0);
        return firstLoaded == null ? 0 : videos.IndexOf(firstLoaded);
    }

    internal async Task UpdateLoadedAsync()
    {
        if (!mayChange) return; // make no changes without change token
        await changeToken!.WaitAsync().ContinueAnywhere();
        Loaded = DateTime.UtcNow;
        hasUnsavedChanges = true;
        changeToken!.Release();
    }

    private void DropVideos(Func<VideoInfo, bool> condition)
    {
        foreach (var video in videos)
            if (condition(video))
                video.PlaylistIndex = null;
    }

    private async ValueTask SaveAsync(Func<Task> save)
    {
        // skip if there are no changes or we don't have a token to make any
        if (!hasUnsavedChanges || !mayChange) return;
        await changeToken!.WaitAsync().ContinueAnywhere();

        try
        {
            await save().ContinueAnywhere();
            hasUnsavedChanges = false;
        }
        finally
        {
            changeToken!.Release();
        }
    }

    // required to enable structurally comparing PlaylistGroup
    public override bool Equals(object? other) => other != null && other is Playlist pl && ThumbnailUrl == pl.ThumbnailUrl;
    public override int GetHashCode() => ThumbnailUrl.GetHashCode(); // because Equals is overridden

    private sealed class ChangeTokenResetter(Playlist playlist, Func<Task> savePlaylist) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await playlist.UpdateShardNumbersAsync().ContinueAnywhere(); // in case user canceled process, leading to early disposal
            await playlist.SaveAsync(savePlaylist).ContinueAnywhere();
            playlist.mayChange = false;
        }
    }

    public sealed class VideoInfo
    {
        [JP("i")] public required string Id { get; set; }
        [JP("s")] public short? ShardNumber { get; set; }
        [JP("u")] public DateTime? Uploaded { get; set; }

        //set if included, null if dropped
        [JP("n")] public uint? PlaylistIndex { get; set; }

        [JP("k")] public string[]? Keywords { get; set; }

        /// <summary>Empty if download succeeded for all.</summary>
        [JP("c")] public CommandScope.CaptionStatus? CaptionTrackDownloadStatus { get; set; }

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

public static class PlaylistExtensions
{
    internal static async Task<IEnumerable<Playlist.VideoInfo>> GetRelevantVideosAsync(this Playlist playlist, PlaylistLikeScope scope)
    {
        var videoInfos = await playlist.GetVideosAsync().ContinueAnywhere();
        return videoInfos.Skip(scope.Skip).Take(scope.Take);
    }

    public static IEnumerable<string> Ids(this IEnumerable<Playlist.VideoInfo> videos) => videos.Select(v => v.Id);
}
