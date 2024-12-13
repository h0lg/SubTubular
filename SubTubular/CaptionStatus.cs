namespace SubTubular;

using CaptionTrackDownloadStatus = (CommandScope.CaptionStatus? status, int videos);

partial class CommandScope
{
    public enum CaptionStatus { UnChecked, None, Error }
}

public static class CaptionStatusExtensions
{
    public static CaptionTrackDownloadStatus[] GetCaptionTrackDownloadStates(this CommandScope scope)
        => scope is PlaylistLikeScope
            ? scope.SingleValidated.Playlist!.GetVideos()
                .GroupBy(v => v.CaptionTrackDownloadStatus)
                .Select(g => (g.Key, g.Count())).ToArray()
            : scope.Validated.Select(vr => vr.Video!)
                .GroupBy(v => v.GetCaptionTrackDownloadStatus())
                .Select(g => (g.Key, g.Count())).ToArray();

    internal static CommandScope.CaptionStatus? GetCaptionTrackDownloadStatus(this Video video)
        => video.CaptionTracks == null ? CommandScope.CaptionStatus.UnChecked
            : video.CaptionTracks.Count == 0 ? CommandScope.CaptionStatus.None
            : video.CaptionTracks.WithErrors().Any() ? CommandScope.CaptionStatus.Error
            : null; // downloaded

    internal static bool IsComplete(this CommandScope.CaptionStatus? status)
        => status is null or CommandScope.CaptionStatus.None;

    public static IEnumerable<CaptionTrackDownloadStatus> Irregular(this CaptionTrackDownloadStatus[] states)
        => states.Where(s => s.status.HasValue); // not downloaded

    public static CommandScope.Notification[] AsNotifications(this IEnumerable<CaptionTrackDownloadStatus> states)
        => states
            .Select(s =>
            {
                var issue = s.status switch
                {
                    null => " all caption tracks dowloaded",
                    CommandScope.CaptionStatus.None => "out caption tracks",
                    CommandScope.CaptionStatus.Error => " errors during caption track download",
                    CommandScope.CaptionStatus.UnChecked => " unchecked caption track status",
                    _ => " unknown caption track status"
                };

                return new CommandScope.Notification($"{s.videos} videos with{issue}");
            })
            .ToArray();

    public static IEnumerable<(CommandScope scope, CaptionTrackDownloadStatus[] captionTrackDlStates)> GetCaptionTrackDownloadStatus(this OutputCommand command)
        => command.GetScopes().Select(scope => (scope, scope.GetCaptionTrackDownloadStates()));

    internal static IEnumerable<CaptionTrack> WithErrors(this IEnumerable<CaptionTrack> tracks)
        => tracks.Where(t => t.Error != null);
}