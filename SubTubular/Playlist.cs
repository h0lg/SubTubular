namespace SubTubular;

[Serializable]
public sealed class Playlist
{
    public required string Title { get; set; }
    public required string ThumbnailUrl { get; set; }
    public string? Channel { get; set; }
    public DateTime Loaded { get; set; }

    /// <summary>The <see cref="Video.Id"/>s and (optional) upload dates
    /// of the videos included in the <see cref="Playlist" />.</summary>
    public IDictionary<string, DateTime?> Videos { get; set; } = new Dictionary<string, DateTime?>();
}
