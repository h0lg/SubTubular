using SubTubular.Extensions;

namespace SubTubular;

public sealed class BatchProgress
{
    public Dictionary<CommandScope, VideoList> VideoLists { get; set; } = [];

    public override string ToString() =>
        VideoLists.Select(list => list.Key.Identify() + " " + list.Value).Join(Environment.NewLine);

    public sealed class VideoList
    {
        public Status State { get; set; } = Status.queued;
        public Dictionary<string, Status>? Videos { get; set; }

        public int AllJobs => Videos?.Count ?? 1;
        public int CompletedJobs => Videos?.Count(v => v.Value == Status.searched) ?? 0;

        public override string ToString() =>
            $"{State} {CompletedJobs}/{AllJobs} - " +
            Videos?.GroupBy(v => v.Value).Select(g => $"{g.Key} " + g.Count()).Join(" - ");
    }

    public enum Status
    {
        queued, loading, downloading, validated, indexing, searching, indexingAndSearching, searched
    }
}

internal class BatchProgressReporter(IProgress<BatchProgress> reporter, BatchProgress batchProgress)
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