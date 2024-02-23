namespace SubTubular;

public sealed class BatchProgress
{
    public required Dictionary<CommandScope, VideoList> VideoLists { get; set; }

    public sealed class VideoList
    {
        public Status State { get; set; } = Status.queued;
        public Dictionary<string, Status>? Videos { get; set; }
    }

    public enum Status { queued, downloading, indexing, searching, indexingAndSearching, searched }
}

internal class BatchProgressReporter(IProgress<BatchProgress> reporter, BatchProgress batchProgress)
{
    internal VideoListProgress CreateVideoListProgress(CommandScope scope)
        => new(batchProgress.VideoLists[scope], new Progress<BatchProgress.VideoList>(listProgress =>
        {
            batchProgress.VideoLists[scope] = listProgress;
            reporter.Report(batchProgress);
            //var playlist = progress.Playlists.Single(pl => pl.Scope == listProgress.Scope);
            //playlist.State = listProgress.State;
            //playlist.Videos = listProgress.Videos;
        }));

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