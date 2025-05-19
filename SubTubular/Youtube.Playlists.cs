using SubTubular.Extensions;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;

namespace SubTubular;

partial class Youtube
{
    internal static string GetPlaylistUrl(string id) => "https://www.youtube.com/playlist?list=" + id;

    internal static string GetChannelUrl(object alias)
    {
        var urlGlue = alias is ChannelHandle ? "@" : alias is ChannelSlug ? "c/"
            : alias is UserName ? "user/" : alias is ChannelId ? "channel/"
            : throw new NotImplementedException($"Generating URL for channel alias {alias.GetType()} is not implemented.");

        return $"https://www.youtube.com/{urlGlue}{alias}";
    }

    /// <summary>Searches videos defined by a playlist.</summary>
    private async Task SearchPlaylistAsync(SearchCommand command, PlaylistLikeScope scope,
        Func<VideoSearchResult, ValueTask> yieldResult, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return;
        var storageKey = scope.StorageKey;
        var playlist = scope.SingleValidated.Playlist!;

        await using (playlist.CreateChangeToken(() => dataStore.SetAsync(storageKey, playlist)))
        {
            try
            {
                Task? continuedRefresh = await RefreshPlaylistAsync(scope, token);
                var videos = playlist.GetVideos().Skip(scope.Skip).Take(scope.Take).ToArray();
                var videoIds = videos.Ids().ToArray();
                scope.QueueVideos(videoIds);
                var spansMultipleShards = false;

                var shardSearches = videos.GroupBy(v => v.ShardNumber).Select(async group =>
                {
                    List<Task> searches = [];
                    var containedVideoIds = group.Ids().ToArray();
                    var completeVideos = group.Where(v => v.CaptionTrackDownloadStatus.IsComplete()).ToArray();
                    var shard = await videoIndexRepo.GetIndexShardAsync(storageKey, group.Key!.Value);
                    var indexedVideoIds = shard.GetIndexed(completeVideos.Ids());

                    if (indexedVideoIds.Length != 0)
                    {
                        var indexedVideoInfos = indexedVideoIds.ToDictionary(id => id, id => group.Single(v => v.Id == id).Uploaded);

                        // search already indexed videos in one go - but on background task to start downloading and indexing videos in parallel
                        searches.Add(SearchIndexedVideos());

                        async Task SearchIndexedVideos()
                        {
                            foreach (var videoId in indexedVideoIds) scope.Report(videoId, VideoList.Status.searching);

                            await foreach (var result in shard.SearchAsync(command, CreateVideoLookup(scope), indexedVideoInfos, playlist, token))
                                await Yield(result);

                            foreach (var videoId in indexedVideoIds) scope.Report(videoId, VideoList.Status.searched);
                        }
                    }

                    var unIndexedVideoIds = containedVideoIds.Except(indexedVideoIds).ToArray();

                    // load, index and search not yet indexed videos
                    if (unIndexedVideoIds.Length > 0)
                    {
                        searches.Add(SearchUnindexedVids());

                        async Task SearchUnindexedVids()
                        {
                            await foreach (var result in SearchUnindexedVideos(command, unIndexedVideoIds, shard, scope, token, playlist))
                                await Yield(result);
                        }
                    }

                    await Task.WhenAll(searches).WithAggregateException().ContinueWith(t =>
                    {
                        shard.Dispose();
                        if (t.IsFaulted) throw t.Exception;
                    });
                }).ToList();

                scope.Report(VideoList.Status.searching);
                spansMultipleShards = shardSearches.Count > 0;

                await foreach (var task in Task.WhenEach(shardSearches))
                {
                    if (task.IsFaulted)
                        throw task.Exception; // raise errors
                }

                scope.Report(token.IsCancellationRequested ? VideoList.Status.canceled : VideoList.Status.searched);
                if (continuedRefresh != null) await continuedRefresh;

                ValueTask Yield(VideoSearchResult result)
                {
                    result.Scope = scope;
                    if (spansMultipleShards) result.Rescore();
                    return yieldResult(result);
                }
            }
            catch (Exception ex) when (!ex.HasInputRootCause()) // bubble up input errors to stop parallel searches
            {
                if (ex.GetRootCauses().AreAll<OperationCanceledException>()) scope.Report(VideoList.Status.canceled);
                else scope.Notify("Errors searching", errors: [ex]);
            }
        }
    }

    internal Task<Playlist> GetPlaylistAsync(PlaylistScope scope, CancellationToken token) =>
        GetPlaylistAsync(scope, async () =>
        {
            var playlist = await Client.Playlists.GetAsync(scope.SingleValidated.Id, token);
            return (playlist.Title, SelectUrl(playlist.Thumbnails), playlist.Author?.ChannelTitle);
        });

    internal Task<Playlist> GetPlaylistAsync(ChannelScope scope, CancellationToken token) =>
        GetPlaylistAsync(scope, async () =>
        {
            var channel = await Client.Channels.GetAsync(scope.SingleValidated.Id, token);
            return (channel.Title, SelectUrl(channel.Thumbnails), null);
        });

    private async Task<Playlist> GetPlaylistAsync(PlaylistLikeScope scope,
        Func<Task<(string title, string thumbnailUrl, string? channel)>> downloadData)
    {
        scope.Report(VideoList.Status.loading);
        var playlist = await dataStore.GetAsync<Playlist>(scope.StorageKey); // get cached
        if (playlist != null) return playlist;

        scope.Report(VideoList.Status.downloading);
        var (title, thumbnailUrl, channel) = await downloadData();
        playlist = new Playlist { Title = title, ThumbnailUrl = thumbnailUrl, Channel = channel };
        await dataStore.SetAsync(scope.StorageKey, playlist);
        return playlist;
    }

    /// <summary>Refreshes the <see cref="Playlist"/> of the given <paramref name="scope"/>
    /// while the <paramref name="token"/> allows it.
    /// Returns null and immediately if the cached video info is fresh enough and sufficient to serve the request.
    /// Otherwise, starts refreshing the videos in the playlist. If the cached video info is sufficient to serve the request
    /// and there aren't any updates after a certain number of videos, returns early while returning the continuing refresh task.
    /// Otherwise, refreshes until sufficient videos are loaded and returns the completed refresh task.</summary>
    private async ValueTask<Task?> RefreshPlaylistAsync(PlaylistLikeScope scope, CancellationToken token)
    {
        var playlist = scope.SingleValidated.Playlist!;
        var requiredVideoCount = (uint)(scope.Skip + scope.Take);

        // return fresh enough playlist with sufficient videos loaded
        if (DateTime.UtcNow.AddHours(-Math.Abs(scope.CacheHours)) <= playlist.Loaded
            && requiredVideoCount <= playlist.GetVideoCount())
        {
            playlist.UpdateShardNumbers(); // in case they weren't before due to an error
            return null; // not changed from previous return
        }

        // playlist cache is outdated or lacking sufficient videos
        scope.Report(VideoList.Status.refreshing);

        var earlyReturn = new SemaphoreSlim(0, 1);

        var paging = Task.Run(async () =>
        {
            uint listIndex = 0;
            var madeChanges = new Queue<bool>(10); // tracks whether adding the last x videos resulted in any changes

            // for canceling paging when we have enough videos while allowing for outside cancellation
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            bool returnedEarly = false, madeChangesAfterEarlyReturn = false;

            try
            {
                // load and update videos in playlist while keeping existing video info
                await foreach (var video in GetVideos(scope, linkedCts.Token))
                {
                    if (linkedCts.Token.IsCancellationRequested) break; // to avoid trying to make changes to playlist without change token
                    if (madeChanges.Count > 9) madeChanges.Dequeue(); // only track the last 10 changes
                    bool changed = playlist.TryAddVideoId(video.Id, listIndex++);
                    madeChanges.Enqueue(changed);

                    if (returnedEarly)
                    {
                        if (changed) madeChangesAfterEarlyReturn = true;
                    }
                    /* return the playlist early because we have enough cached info to serve the request scope
                     * and can reasonably assume that the cache is up to date
                     * because adding the last n videos didn't result in any changes */
                    else if (requiredVideoCount <= playlist.GetVideoCount() && madeChanges.All(x => !x))
                    {
                        playlist.UpdateShardNumbers();
                        earlyReturn.Release();
                        returnedEarly = true;
                    }

                    if (listIndex > requiredVideoCount)
                    {
                        linkedCts.Cancel(); // cancel paging
                        break; // stop enumerating
                    }
                }
            }
            catch (Exception ex) { scope.Notify("Error refreshing playlist", ex.Message, [ex]); }
            finally
            {
                // to enable indexing new videos - can't succeed if playlist change token has been revoked
                if (!token.IsCancellationRequested) playlist.UpdateShardNumbers();

                if (returnedEarly && madeChangesAfterEarlyReturn) scope.Notify("Results may be stale.",
                    "The command was run on cached playlist info that turned out to be stale - you may want re-run it."
                    + $" {AssemblyInfo.Name} decided to do so when hitting known video IDs during playlist refresh to get you quicker results"
                    + " - but completing the refresh in the background turned up with unexpected changes.");

                earlyReturn.Release(); // to stop waiting below
            }
        }, token);

        // wait on empty semaphore instead of task to enable early return
        await earlyReturn.WaitAsync(token);

        playlist.UpdateLoaded();
        return paging; // to enable continuing to wait for refresh to finish on early return
    }

    private IAsyncEnumerable<PlaylistVideo> GetVideos(PlaylistLikeScope scope, CancellationToken token) => scope switch
    {
        ChannelScope _ => Client.Channels.GetUploadsAsync(scope.SingleValidated.Id, token),
        PlaylistScope _ => Client.Playlists.GetVideosAsync(scope.SingleValidated.Id, token),
        _ => throw new NotImplementedException($"Getting videos for the {scope.GetType()} is not implemented.")
    };

    /// <summary>Returns a curried <see cref="GetVideoAsync(string, CancellationToken, CommandScope, bool)"/>
    /// with the <paramref name="scope"/> supplied.</summary>
    private Func<string, CancellationToken, Task<Video>> CreateVideoLookup(CommandScope scope)
        => (videoId, token) => GetVideoAsync(videoId, token, scope);
}
