namespace SubTubular;

/// <summary>Tracks the progress of distinct <see cref="CommandScope"/>s in an <see cref="OutputCommand"/>.</summary>
public sealed class BatchProgress
{
    public Dictionary<CommandScope, VideoList> VideoLists { get; set; } = [];

    /// <summary>Represents the progress of a <see cref="CommandScope"/>.</summary>
    public sealed class VideoList
    {
        public Status State { get; set; } = Status.queued;

        /// <summary>Represents the progress of individual <see cref="Video.Id"/>s in a <see cref="CommandScope"/>s.</summary>
        public Dictionary<string, Status>? Videos { get; set; }
    }

    public enum Status { queued, loading, downloading, validated, refreshing, indexing, searching, indexingAndSearching, searched }
}

/// <summary>A <see cref="VideoListProgress"/> factory for the <see cref="BatchProgress.VideoLists"/> of <paramref name="batchProgress"/>
/// delegating to the <paramref name="reporter"/> to bundle all progress reports in it.</summary>
internal class BatchProgressReporter(BatchProgress batchProgress, IProgress<BatchProgress> reporter)
{
    internal VideoListProgress CreateVideoListProgress(CommandScope scope)
    {
        if (!batchProgress.VideoLists.TryGetValue(scope, out var videoList))
            videoList = new BatchProgress.VideoList();

        return new(videoList, new Progress<BatchProgress.VideoList>(listProgress =>
        {
            batchProgress.VideoLists[scope] = listProgress;
            reporter.Report(batchProgress);
            //var playlist = progress.Playlists.Single(pl => pl.Scope == listProgress.Scope);
            //playlist.State = listProgress.State;
            //playlist.Videos = listProgress.Videos;
        }));
    }

    /// <summary>Pairs the <paramref name="videoList"/> with the <paramref name="reporter"/> for convenient progress reporting.</summary>
    internal class VideoListProgress(BatchProgress.VideoList videoList, IProgress<BatchProgress.VideoList> reporter)
    {
        public BatchProgress.VideoList VideoList { get; } = videoList;

        internal void SetVideos(IEnumerable<string> videoIds)
        {
            VideoList.Videos = videoIds.ToDictionary(id => id, _ => BatchProgress.Status.queued);
            ReportChange();
        }

        internal void Report(BatchProgress.Status state)
        {
            VideoList.State = state;
            ReportChange();
        }

        internal void Report(string videoId, BatchProgress.Status state)
        {
            UpdateVideoState(videoId, state);
            ReportChange();
        }

        internal void Report(IEnumerable<Video> videos, BatchProgress.Status state)
        {
            foreach (var video in videos) UpdateVideoState(video.Id, state);
            ReportChange();
        }

        private void UpdateVideoState(string videoId, BatchProgress.Status state) => VideoList.Videos![videoId] = state;
        private void ReportChange() => reporter.Report(VideoList);
    }
}