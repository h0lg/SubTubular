using System.Text.Json.Serialization;

namespace SubTubular;

using JP = JsonPropertyNameAttribute;

public sealed class Playlist
{
    [JP("t")] public required string Title { get; set; }
    [JP("u")] public required string ThumbnailUrl { get; set; }
    [JP("c")] public string? Channel { get; set; }
    [JP("l")] public DateTime Loaded { get; set; }

    /// <summary>The <see cref="Video.Id"/>s and (optional) upload dates
    /// of the videos included in the <see cref="Playlist" />.</summary>
    [JP("v")] public IDictionary<string, DateTime?> Videos { get; set; } = new Dictionary<string, DateTime?>();

    internal IEnumerable<string> GetVideoIds() => Videos.Keys;
    internal DateTime? GetVideoUploaded(string id) => Videos.TryGetValue(id, out var uploaded) ? uploaded : null;

    internal void AddVideoIds(string[] freshKeys)
    {
        // use new order but append older entries; note that this leaves remotely deleted videos in the playlist
        Videos = freshKeys.Concat(GetVideoIds().Except(freshKeys)).ToDictionary(id => id, GetVideoUploaded);
    }

    internal bool SetUploaded(Video video)
    {
        if (Videos[video.Id] != video.Uploaded)
        {
            Videos[video.Id] = video.Uploaded;
            return true;
        }

        return false;
    }
}
