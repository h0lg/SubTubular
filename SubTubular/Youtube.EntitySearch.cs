using SubTubular.Extensions;
using YoutubeExplode.Common;

namespace SubTubular;

partial class Youtube
{
    public async Task<IEnumerable<YoutubeSearchResult>> SearchForChannelsAsync(string text, CancellationToken token)
        => await SearchedForCachedAsync(text, ChannelScope.StorageKeyPrefix, async text =>
        {
            var channels = await Client.Search.GetChannelsAsync(text, token);
            return channels.Select(c => new YoutubeSearchResult(c.Id, c.Title, c.Url, SelectUrl(c.Thumbnails)));
        }, token);

    public async Task<IEnumerable<YoutubeSearchResult>> SearchForPlaylistsAsync(string text, CancellationToken token)
        => await SearchedForCachedAsync(text, PlaylistScope.StorageKeyPrefix, async text =>
        {
            var playlists = await Client.Search.GetPlaylistsAsync(text, token);
            return playlists.Select(pl => new YoutubeSearchResult(pl.Id, pl.Title, pl.Url, SelectUrl(pl.Thumbnails), pl.Author?.ChannelTitle));
        }, token);

    public async Task<IEnumerable<YoutubeSearchResult>> SearchForVideosAsync(string text, CancellationToken token)
        => await SearchedForCachedAsync(text, Video.StorageKeyPrefix, async text =>
        {
            var videos = await Client.Search.GetVideosAsync(text, token);
            return videos.Select(v => new YoutubeSearchResult(v.Id, v.Title, v.Url, SelectUrl(v.Thumbnails), v.Author?.ChannelTitle));
        }, token);

    /// <summary>Identifies scope search caches by being the second prefix after the StorageKeyPrefix identifying the scope type.</summary>
    public const string SearchAffix = "search ";

    private async Task<YoutubeSearchResult[]> SearchedForCachedAsync(string text, string keyPrefix,
        Func<string, Task<IEnumerable<YoutubeSearchResult>>> searchYoutubeAsync, CancellationToken token)
    {
        if (token.IsCancellationRequested) return [];
        string key = keyPrefix + SearchAffix + text.ToFileSafe();
        var cached = await dataStore.GetAsync<YoutubeSearchResult.Cache>(key);

        if (cached == null || cached.Search != text || cached.Created.AddHours(1) < DateTime.Now)
        {
            if (token.IsCancellationRequested) return [];
            var mapped = await searchYoutubeAsync(text);
            cached = new YoutubeSearchResult.Cache(text, [.. mapped], DateTime.Now);
            await dataStore.SetAsync(key, cached);
        }

        return cached.Results;
    }
}
