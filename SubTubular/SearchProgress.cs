using SubTubular.Extensions;

namespace SubTubular;

/// <summary>Represents the progress of a <see cref="CommandScope"/>.</summary>
public sealed class VideoList
{
    public Status State { get; set; } = Status.queued;

    /// <summary>Represents the progress of individual <see cref="Video.Id"/>s in a <see cref="CommandScope"/>s.</summary>
    public Dictionary<string, Status>? Videos { get; set; }

    public int AllJobs => Videos?.Count ?? 1; // default to one job for the VideoList itself
    public int CompletedJobs => Videos?.Count(v => v.Value == Status.searched) ?? 0;

    // used for display in UI
    public override string ToString()
    {
        var videos = Videos?.Where(v => v.Value != State).GroupBy(v => v.Value).Select(g => $"{Display(g.Key)} {g.Count()}").Join(" | ");
        return $"{Display(State)} {CompletedJobs}/{AllJobs}" + (videos.IsNullOrEmpty() ? null : (" - " + videos));
    }

    public enum Status { queued, loading, downloading, validated, refreshing, indexing, searching, indexingAndSearching, searched }

    // used for display in UI
    private static string Display(Status status) => status switch
    {
        Status.indexingAndSearching => "indexing and searching",
        _ => status.ToString()
    };
}

partial class CommandScope
{
    public VideoList VideoList { get; } = new();
    public event EventHandler? ProgressChanged;

    internal void SetVideos(IEnumerable<string> videoIds)
    {
        VideoList.Videos = videoIds.ToDictionary(id => id, _ => VideoList.Status.queued);
        ReportChange();
    }

    internal void Report(VideoList.Status state)
    {
        VideoList.State = state;
        ReportChange();
    }

    internal void Report(string videoId, VideoList.Status state)
    {
        UpdateVideoState(videoId, state);
        ReportChange();
    }

    internal void Report(IEnumerable<Video> videos, VideoList.Status state)
    {
        foreach (var video in videos) UpdateVideoState(video.Id, state);
        ReportChange();
    }

    private void UpdateVideoState(string videoId, VideoList.Status state) => VideoList.Videos![videoId] = state;
    private void ReportChange() => ProgressChanged?.Invoke(this, EventArgs.Empty);
}