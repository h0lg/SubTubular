namespace SubTubular;

using CaptionStatus = CommandScope.CaptionStatus;
using CaptionTrackDownloadStatus = (CommandScope.CaptionStatus? status, int videos);
using Levels = CommandScope.Notification.Levels;

partial class CommandScope
{
    public enum CaptionStatus { UnChecked, None, Error }
}

public static class CaptionStatusExtensions
{
    public static CaptionTrackDownloadStatus[] GetCaptionTrackDownloadStates(this CommandScope scope)
        => [.. scope is PlaylistLikeScope
            ? scope.SingleValidated.Playlist!.GetVideos()
                .GroupBy(v => v.CaptionTrackDownloadStatus)
                .Select(g => (g.Key, g.Count()))
            : scope.Validated.Select(vr => vr.Video!)
                .GroupBy(v => v.GetCaptionTrackDownloadStatus())
                .Select(g => (g.Key, g.Count())) ];

    internal static CaptionStatus? GetCaptionTrackDownloadStatus(this Video video)
        => video.CaptionTracks == null ? CaptionStatus.UnChecked
            : video.CaptionTracks.Count == 0 ? CaptionStatus.None
            : video.CaptionTracks.WithErrors().Any() ? CaptionStatus.Error
            : null; // downloaded

    internal static bool IsComplete(this CaptionStatus? status)
        => status is null or CaptionStatus.None;

    public static IEnumerable<CaptionTrackDownloadStatus> Irregular(this CaptionTrackDownloadStatus[] states)
        => states.Where(s => s.status.HasValue); // not downloaded

    public static CommandScope.Notification[] AsNotifications(this IEnumerable<CaptionTrackDownloadStatus> states)
        => [.. states
            .Select(s =>
            {
                var (level, issue) = s.status switch
                {
                    null => (Levels.Info, " all caption tracks dowloaded"),
                    CaptionStatus.None => (Levels.Info, "out caption tracks"),
                    CaptionStatus.Error => (Levels.Error, " errors during caption track download"),
                    CaptionStatus.UnChecked => (Levels.Warning, " unchecked caption track status"),
                    _ => (Levels.Error, " unknown caption track status")
                };

                return new CommandScope.Notification($"{s.videos} videos with{issue}", level: level);
            })];

    public static IEnumerable<(CommandScope scope, CaptionTrackDownloadStatus[] captionTrackDlStates)> GetCaptionTrackDownloadStatus(this OutputCommand command)
        => command.GetScopes().Select(scope => (scope, scope.GetCaptionTrackDownloadStates()));

    internal static IEnumerable<CaptionTrack> WithErrors(this IEnumerable<CaptionTrack> tracks)
        => tracks.Where(t => t.Error != null);
}