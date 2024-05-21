using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

/// <summary>Represents the progress of a <see cref="CommandScope"/>.</summary>
public sealed class VideoList
{
    public Status State { get; set; } = Status.queued;

    /// <summary>Represents the progress of individual <see cref="Video.Id"/>s in a <see cref="CommandScope"/>s.</summary>
    public Dictionary<string, Status>? Videos { get; set; }

    public int AllJobs => Videos?.Count ?? 1; // default to one job for the VideoList itself

    public int CompletedJobs
    {
        get
        {
            if (Videos == null) return State == Status.validated ? 1 : 0;
            var targetState = State > Status.validated ? Status.searched : Status.validated;
            return Videos?.Count(v => v.Value == targetState) ?? 0;
        }
    }

    // used for display in UI
    public override string ToString()
    {
        var videos = Videos?.Where(v => v.Value != State).GroupBy(v => v.Value).Select(g => $"{Label(g.Key)} {g.Count()}").Join(" | ");
        var completed = Videos == null ? string.Empty : $" {CompletedJobs}/{AllJobs}";
        return $"{Label(State)}" + completed + (videos.IsNullOrEmpty() ? null : (" - " + videos));
    }

    /// <summary>States of a <see cref="CommandScope"/> or individual <see cref="Video"/>s.</summary>
    public enum Status
    {
        queued, preValidated,
        loading, downloading, validated,
        refreshing, indexing, searching, indexingAndSearching,
        searched
    }

    // used for display in UI
    private static string Label(Status status) => status switch
    {
        Status.preValidated => "pre-validated",
        Status.indexingAndSearching => "indexing and searching",
        _ => status.ToString()
    };
}

partial class CommandScope
{
    [JsonIgnore]
    public VideoList Progress { get; } = new();

    public event EventHandler? ProgressChanged;

    internal void QueueVideos(IEnumerable<string> videoIds)
    {
        if (Progress.Videos == null) Progress.Videos = [];

        foreach (var id in videoIds)
            if (!Progress.Videos.ContainsKey(id))
                Progress.Videos[id] = VideoList.Status.queued;

        ReportChange();
    }

    internal void Report(VideoList.Status state)
    {
        Progress.State = state;
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

    private void UpdateVideoState(string videoId, VideoList.Status state) => Progress.Videos![videoId] = state;
    private void ReportChange() => ProgressChanged?.Invoke(this, EventArgs.Empty);
}