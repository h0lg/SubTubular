using SubTubular.Extensions;

namespace SubTubular;

using CaptionTrackDownloadStatus = (CommandScope.CaptionStatus? status, int videos);

partial class CommandScope
{
    public enum CaptionStatus { UnChecked, None, Error }
}

public static class CaptionStatusExtensions
{
    public static CaptionTrackDownloadStatus[] GetCaptionTrackDownloadStates(this Playlist playlist)
        => playlist.GetVideos()
            .GroupBy(v => v.CaptionTrackDownloadStatus)
            .Select(g => (g.Key, g.Count())).ToArray();

    internal static CommandScope.CaptionStatus? GetCaptionTrackDownloadStatus(this Video video)
        => video.CaptionTracks == null ? CommandScope.CaptionStatus.UnChecked
            : video.CaptionTracks.Count == 0 ? CommandScope.CaptionStatus.None
            : video.CaptionTracks.WithErrors().Any() ? CommandScope.CaptionStatus.Error
            : null;

    internal static bool IsComplete(this CommandScope.CaptionStatus? status)
        => status is null or CommandScope.CaptionStatus.None;

    public static CommandScope.Notification[] AsNotifications(this CaptionTrackDownloadStatus[] states,
        Func<CaptionTrackDownloadStatus, bool>? predicate = null)
        => states
            .Where(s => predicate == null || predicate(s))
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

    public static Dictionary<PlaylistLikeScope, CaptionTrackDownloadStatus[]> GetCaptionTrackDownloadStatus(this OutputCommand command)
        => command.GetPlaylistLikeScopes().ToDictionary(scope => scope, scope => scope.SingleValidated.Playlist!.GetCaptionTrackDownloadStates());
}