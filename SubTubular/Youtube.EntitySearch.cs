using SubTubular.Extensions;
using YoutubeExplode.Common;

namespace SubTubular;

partial class Youtube
{
    public async Task<IEnumerable<YoutubeSearchResult>> SearchForChannelsAsync(string text, CancellationToken cancellation)
        => await SearchedForCachedAsync(text, ChannelScope.StorageKeyPrefix, async (string text, CancellationToken cancellation) =>
        {
            var channels = await Client.Search.GetChannelsAsync(text, cancellation);
            return channels.Select(c => new YoutubeSearchResult(c.Id, c.Title, c.Url, SelectUrl(c.Thumbnails)));
        }, cancellation);

    public async Task<IEnumerable<YoutubeSearchResult>> SearchForPlaylistsAsync(string text, CancellationToken cancellation)
        => await SearchedForCachedAsync(text, PlaylistScope.StorageKeyPrefix, async (string text, CancellationToken cancellation) =>
        {
            var playlists = await Client.Search.GetPlaylistsAsync(text, cancellation);
            return playlists.Select(pl => new YoutubeSearchResult(pl.Id, pl.Title, pl.Url, SelectUrl(pl.Thumbnails), pl.Author?.ChannelTitle));
        }, cancellation);

    public async Task<IEnumerable<YoutubeSearchResult>> SearchForVideosAsync(string text, CancellationToken cancellation)
        => await SearchedForCachedAsync(text, Video.StorageKeyPrefix, async (string text, CancellationToken cancellation) =>
        {
            var videos = await Client.Search.GetVideosAsync(text, cancellation);
            return videos.Select(v => new YoutubeSearchResult(v.Id, v.Title, v.Url, SelectUrl(v.Thumbnails), v.Author?.ChannelTitle));
        }, cancellation);

    /// <summary>Identifies scope search caches by being the second prefix after the StorageKeyPrefix identifying the scope type.</summary>
    public const string SearchAffix = "search ";

    private async Task<YoutubeSearchResult[]> SearchedForCachedAsync(string text, string keyPrefix,
        Func<string, CancellationToken, Task<IEnumerable<YoutubeSearchResult>>> searchYoutubeAsync, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return [];
        string key = keyPrefix + SearchAffix + text.ToFileSafe();
        var cached = await dataStore.GetAsync<YoutubeSearchResult.Cache>(key);

        if (cached == null || cached.Search != text || cached.Created.AddHours(1) < DateTime.Now)
        {
            if (cancellation.IsCancellationRequested) return [];
            var mapped = await searchYoutubeAsync(text, cancellation);
            cached = new YoutubeSearchResult.Cache(text, mapped.ToArray(), DateTime.Now);
            await dataStore.SetAsync(key, cached);
        }

        return cached.Results;
    }
}
