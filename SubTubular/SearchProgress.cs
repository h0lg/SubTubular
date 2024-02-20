namespace SubTubular;

public sealed class SearchProgress
{
    public required Dictionary<CommandScope, Playlist> Playlists { get; set; }

    public sealed class Playlist
    {
        public Status State { get; set; } = Status.queued;
        public Dictionary<string, Status>? Videos { get; set; }
    }

    public enum Status { queued, downloading, indexing, searching, indexingAndSearching, searched }
}

internal class PlaylistBatchProgress(IProgress<SearchProgress> reporter, SearchProgress searchProgress)
{
    internal PlaylistProgress CreatePlaylistProgress(CommandScope scope)
        => new(searchProgress.Playlists[scope], new Progress<SearchProgress.Playlist>(listProgress =>
        {
            searchProgress.Playlists[scope] = listProgress;
            reporter.Report(searchProgress);
            //var playlist = progress.Playlists.Single(pl => pl.Scope == listProgress.Scope);
            //playlist.State = listProgress.State;
            //playlist.Videos = listProgress.Videos;
        }));

    internal class PlaylistProgress(SearchProgress.Playlist playlist, IProgress<SearchProgress.Playlist> reporter)
    {
        public SearchProgress.Playlist Playlist { get; } = playlist;

        internal void SetVideos(IEnumerable<string> videoIds)
        {
            Playlist.Videos = videoIds.ToDictionary(id => id, _ => SearchProgress.Status.queued);
            ReportChange();
        }

        internal void Report(SearchProgress.Status state)
        {
            Playlist.State = state;
            ReportChange();
        }

        internal void Report(string videoId, SearchProgress.Status state)
        {
            UpdateVideoState(videoId, state);
            ReportChange();
        }

        internal void Report(IEnumerable<Video> videos, SearchProgress.Status state)
        {
            foreach (var video in videos) UpdateVideoState(video.Id, state);
            ReportChange();
        }

        private void UpdateVideoState(string videoId, SearchProgress.Status state) => Playlist.Videos![videoId] = state;
        private void ReportChange() => reporter.Report(Playlist);
    }
}