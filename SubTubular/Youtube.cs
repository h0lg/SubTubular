using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SubTubular.Extensions;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using Pipe = System.Threading.Channels.Channel; // to avoid conflict with YoutubeExplode.Channels.Channel

namespace SubTubular;

public sealed partial class Youtube(DataStore dataStore, VideoIndexRepository videoIndexRepo)
{
    public static string GetVideoUrl(string videoId) => "https://youtu.be/" + videoId;
    internal static string GetPlaylistUrl(string id) => "https://www.youtube.com/playlist?list=" + id;

    internal static string GetChannelUrl(object alias)
    {
        var urlGlue = alias is ChannelHandle ? "@" : alias is ChannelSlug ? "c/"
            : alias is UserName ? "user/" : alias is ChannelId ? "channel/"
            : throw new NotImplementedException($"Generating URL for channel alias {alias.GetType()} is not implemented.");

        return $"https://www.youtube.com/{urlGlue}{alias}";
    }

    public readonly YoutubeClient Client = new();

    public async IAsyncEnumerable<VideoSearchResult> SearchAsync(SearchCommand command,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        using var linkedTs = CancellationTokenSource.CreateLinkedTokenSource(cancellation); // to cancel parallel searches on InputException
        var results = Pipe.CreateUnbounded<VideoSearchResult>(new UnboundedChannelOptions() { SingleReader = true });
        Func<VideoSearchResult, ValueTask> addResult = r => results.Writer.WriteAsync(r, linkedTs.Token);
        List<Task> searches = [];
        SearchPlaylistLikeScopes(command.Channels);
        SearchPlaylistLikeScopes(command.Playlists);

        if (command.HasValidVideos)
        {
            command.Videos!.ResetProgressAndNotifications();
            searches.Add(SearchVideosAsync(command, addResult, linkedTs.Token));
        }

        var spansMultipleIndexes = searches.Count > 0;

        var searching = Task.Run(async () =>
        {
            await foreach (var task in Task.WhenEach(searches))
            {
                if (task.IsFaulted)
                {
                    if (task.Exception.HasInputRootCause())
                    {
                        /* wait for the root cause to bubble up instead of triggering
                         * an OperationCanceledException further up the call chain. */
                        linkedTs.Cancel(); // cancel parallel searches if query parser yields input error
                    }

                    throw task.Exception; // bubble up errors
                }
            }
        }, linkedTs.Token).ContinueWith(t =>
        {
            results.Writer.Complete();
            if (t.IsFaulted) throw t.Exception; // bubble up errors
        });

        // don't pass cancellation token to avoid throwing before searching is awaited below
        await foreach (var result in results.Reader.ReadAllAsync())
        {
            if (linkedTs.Token.IsCancellationRequested) break; // end loop gracefully to throw below
            if (spansMultipleIndexes) result.Rescore();
            yield return result;
        }

        await searching; // throws the relevant input errors

        void SearchPlaylistLikeScopes(PlaylistLikeScope[]? scopes)
        {
            if (scopes.HasAny())
                foreach (var scope in scopes!)
                {
                    scope.ResetProgressAndNotifications(); // to prevent state from prior searches from bleeding into this one
                    searches.Add(SearchPlaylistAsync(command, scope, addResult, linkedTs.Token));
                }
        }
    }

    /// <summary>Searches videos defined by a playlist.</summary>
    private async Task SearchPlaylistAsync(SearchCommand command, PlaylistLikeScope scope,
        Func<VideoSearchResult, ValueTask> yieldResult, CancellationToken cancellation = default)
    {
        if (cancellation.IsCancellationRequested) return;
        var storageKey = scope.StorageKey;
        var playlist = scope.SingleValidated.Playlist!;

        await using (playlist.CreateChangeToken(() => dataStore.SetAsync(storageKey, playlist)))
        {
            try
            {
                Task? continuedRefresh = await RefreshPlaylistAsync(scope, cancellation);
                var videos = playlist.GetVideos().Skip(scope.Skip).Take(scope.Take).ToArray();
                var videoIds = videos.Ids().ToArray();
                scope.QueueVideos(videoIds);
                var spansMultipleIndexes = false;

                var searches = videos.GroupBy(v => v.ShardNumber).Select(async group =>
                {
                    List<Task> shardSearches = [];
                    var containedVideoIds = group.Ids().ToArray();
                    var completeVideos = group.Where(v => v.CaptionTrackDownloadStatus.IsComplete()).ToArray();
                    var shard = await videoIndexRepo.GetIndexShardAsync(storageKey, group.Key!.Value);
                    var indexedVideoIds = shard.GetIndexed(completeVideos.Ids());

                    if (indexedVideoIds.Length != 0)
                    {
                        var indexedVideoInfos = indexedVideoIds.ToDictionary(id => id, id => group.Single(v => v.Id == id).Uploaded);

                        // search already indexed videos in one go - but on background task to start downloading and indexing videos in parallel
                        shardSearches.Add(SearchIndexedVideos());

                        async Task SearchIndexedVideos()
                        {
                            foreach (var videoId in indexedVideoIds) scope.Report(videoId, VideoList.Status.searching);

                            await foreach (var result in shard.SearchAsync(command, CreateVideoLookup(scope), indexedVideoInfos, playlist, cancellation))
                                await Yield(result);

                            foreach (var videoId in indexedVideoIds) scope.Report(videoId, VideoList.Status.searched);
                        }
                    }

                    var unIndexedVideoIds = containedVideoIds.Except(indexedVideoIds).ToArray();

                    // load, index and search not yet indexed videos
                    if (unIndexedVideoIds.Length > 0)
                    {
                        shardSearches.Add(SearchUnindexedVids());

                        async Task SearchUnindexedVids()
                        {
                            await foreach (var result in SearchUnindexedVideos(command, unIndexedVideoIds, shard, scope, cancellation, playlist))
                                await Yield(result);
                        }
                    }

                    await Task.WhenAll(shardSearches).WithAggregateException().ContinueWith(t =>
                    {
                        shard.Dispose();
                        if (t.IsFaulted) throw t.Exception;
                    });
                }).ToList();

                scope.Report(VideoList.Status.searching);
                spansMultipleIndexes = searches.Count > 0;

                await foreach (var task in Task.WhenEach(searches))
                {
                    if (task.IsFaulted)
                        throw task.Exception; // raise errors
                }

                scope.Report(cancellation.IsCancellationRequested ? VideoList.Status.canceled : VideoList.Status.searched);
                if (continuedRefresh != null) await continuedRefresh;

                ValueTask Yield(VideoSearchResult result)
                {
                    result.Scope = scope;
                    if (spansMultipleIndexes) result.Rescore();
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

    internal Task<Playlist> GetPlaylistAsync(PlaylistScope scope, CancellationToken cancellation) =>
        GetPlaylistAsync(scope, async () =>
        {
            var playlist = await Client.Playlists.GetAsync(scope.SingleValidated.Id, cancellation);
            return (playlist.Title, SelectUrl(playlist.Thumbnails), playlist.Author?.ChannelTitle);
        });

    internal Task<Playlist> GetPlaylistAsync(ChannelScope scope, CancellationToken cancellation) =>
        GetPlaylistAsync(scope, async () =>
        {
            var channel = await Client.Channels.GetAsync(scope.SingleValidated.Id, cancellation);
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

    private IAsyncEnumerable<PlaylistVideo> GetVideos(PlaylistLikeScope scope, CancellationToken cancellation) => scope switch
    {
        ChannelScope _ => Client.Channels.GetUploadsAsync(scope.SingleValidated.Id, cancellation),
        PlaylistScope _ => Client.Playlists.GetVideosAsync(scope.SingleValidated.Id, cancellation),
        _ => throw new NotImplementedException($"Getting videos for the {scope.GetType()} is not implemented.")
    };

    private async IAsyncEnumerable<VideoSearchResult> SearchUnindexedVideos(SearchCommand command,
        string[] unIndexedVideoIds, VideoIndex index, CommandScope scope,
        [EnumeratorCancellation] CancellationToken cancellation,
        Playlist? playlist = default)
    {
        if (cancellation.IsCancellationRequested) yield break;
        scope.Report(VideoList.Status.indexingAndSearching);

        /* limit channel capacity to avoid holding a lot of loaded but unprocessed videos in memory
            SingleReader because we're reading from it synchronously */
        const int queueSize = 10;
        var unIndexedVideos = Pipe.CreateBounded<Video>(new BoundedChannelOptions(queueSize) { SingleReader = true });

        // load videos asynchronously in the background and put them on the unIndexedVideos channel for processing
        var loadVideos = Task.Run(async () =>
        {
            var loadLimiter = new SemaphoreSlim(queueSize, queueSize);

            var downloads = unIndexedVideoIds.Select(id => Task.Run(async () =>
            {
                /*  pause task here before starting download until channel accepts another video
                    to avoid holding a lot of loaded but unprocessed videos in memory */
                await loadLimiter.WaitAsync();

                try
                {
                    Video? video = command.Videos?.Validated.SingleOrDefault(v => v.Id == id)?.Video;
                    video ??= await GetVideoAsync(id, cancellation, scope, downloadCaptionTracksAndSave: false);

                    // re/download caption tracks for the video
                    if (!video.GetCaptionTrackDownloadStatus().IsComplete())
                        await DownloadCaptionTracksAndSaveAsync(video, scope, cancellation);

                    playlist?.Update(video);

                    await unIndexedVideos.Writer.WriteAsync(video);
                    scope.Report(id, VideoList.Status.indexing);
                }
                /* only start another download if channel has accepted the video or an error occurred */
                finally { loadLimiter.Release(); }
            }, cancellation));

            await Task.WhenAll(downloads).WithAggregateException()
               .ContinueWith(t =>
               {
                   // complete writing after all download tasks finished
                   unIndexedVideos.Writer.Complete();
                   if (t.Exception != null) throw t.Exception;
               }, cancellation);
        });

        var uncommitted = new List<Video>(); // batch of loaded and indexed, but uncommitted video index changes

        // local getter reusing already loaded video from uncommitted bag for better performance
        Func<string, CancellationToken, Task<Video>> getVideoAsync = CreateVideoLookup(uncommitted);

        // read synchronously from the channel because we're writing to the same video index
        await foreach (var video in unIndexedVideos.Reader.ReadAllAsync())
        {
            if (cancellation.IsCancellationRequested) break;
            if (uncommitted.Count == 0) index.BeginBatchChange();
            await index.AddOrUpdateAsync(video, cancellation);
            uncommitted.Add(video);

            // save batch of changes
            if (uncommitted.Count >= queueSize // to prevent the batch from growing too big
                || unIndexedVideos.Reader.Completion.IsCompleted // to save remaining changes
                || unIndexedVideos.Reader.Count == 0) // to use resources efficiently while we've got nothing queued up for indexing
            {
                await index.CommitBatchChangeAsync();

                var indexedVideoInfos = uncommitted.ToDictionary(v => v.Id, v => v.Uploaded as DateTime?);
                scope.Report(uncommitted, VideoList.Status.searching);

                // search after committing index changes to output matches as we go
                await foreach (var result in index.SearchAsync(command, getVideoAsync, indexedVideoInfos, cancellation: cancellation))
                    yield return result;

                scope.Report(uncommitted, VideoList.Status.searched);
                uncommitted.Clear(); // safe to do because we're reading synchronously and no other thread could have added to it in between
            }
        }

        await loadVideos; // just to re-throw possible exceptions; should have completed at this point
    }

    /// <summary>Searches videos scoped by the specified <paramref name="command"/>.</summary>
    private async Task SearchVideosAsync(SearchCommand command,
        Func<VideoSearchResult, ValueTask> yieldResult, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return;
        VideosScope scope = command.Videos!;
        var videoIds = scope.GetRemoteValidated().Ids().ToArray();
        scope.QueueVideos(videoIds);
        var storageKey = Video.StorageKeyPrefix + videoIds.Order().Join(" ");
        var index = await videoIndexRepo.GetAsync(storageKey);

        Task searching;

        if (index == null)
        {
            index = videoIndexRepo.Build(storageKey);

            searching = Task.Run(async () =>
            {
                await foreach (var result in SearchUnindexedVideos(command, videoIds, index, scope, cancellation))
                    await yieldResult(result);
            }, cancellation);
        }
        else searching = Task.Run(async () =>
        {
            scope.Report(VideoList.Status.searching);

            // indexed videos are assumed to have downloaded their caption tracks already
            Video[] videos = scope.Validated.Select(v => v.Video!).ToArray();

            await foreach (var result in index.SearchAsync(command, CreateVideoLookup(videos), cancellation: cancellation))
                await yieldResult(result);
        }, cancellation);

        scope.Report(VideoList.Status.searched);

        try
        {
            await searching; // to throw exceptions
        }
        catch (Exception ex) when (!ex.HasInputRootCause()) // bubble up input errors to stop parallel searches
        {
            var causes = ex.GetRootCauses();

            if (causes.AreAll<OperationCanceledException>()) scope.Report(VideoList.Status.canceled);
            else scope.Notify("Errors searching", errors: [ex]);
        }

        index.Dispose();
    }

    internal async Task<Video> GetVideoAsync(string videoId, CancellationToken cancellation,
        CommandScope scope, bool downloadCaptionTracksAndSave = true)
    {
        cancellation.ThrowIfCancellationRequested();
        var storageKey = Video.StorageKeyPrefix + videoId;
        scope.Report(videoId, VideoList.Status.loading);
        var video = await dataStore.GetAsync<Video>(storageKey);

        if (video == null)
        {
            scope.Report(videoId, VideoList.Status.downloading);

            var vid = await Client.Videos.GetAsync(videoId, cancellation);
            video = MapVideo(vid);
            video.UnIndexed = true; // to re-index it if it was already indexed
            if (downloadCaptionTracksAndSave) await DownloadCaptionTracksAndSaveAsync(video, scope, cancellation);
        }

        return video;
    }

    private static Video MapVideo(YoutubeExplode.Videos.Video video) => new()
    {
        Id = video.Id.Value,
        Title = video.Title,
        Description = video.Description,
        Keywords = [.. video.Keywords],
        Uploaded = video.UploadDate.UtcDateTime,
        Channel = video.Author.ChannelTitle,
        Thumbnail = SelectUrl(video.Thumbnails)
    };

    private async Task DownloadCaptionTracksAndSaveAsync(Video video, CommandScope scope, CancellationToken cancellation)
    {
        List<Exception> errors = [];

        try
        {
            var trackManifest = await Client.Videos.ClosedCaptions.GetManifestAsync(video.Id, cancellation);
            video.CaptionTracks = [];

            foreach (var trackInfo in trackManifest.Tracks)
            {
                var captionTrack = new CaptionTrack { LanguageName = trackInfo.Language.Name, Url = trackInfo.Url };

                try
                {
                    // Get the actual closed caption track
                    var track = await Client.Videos.ClosedCaptions.GetAsync(trackInfo, cancellation);

                    captionTrack.Captions = track.Captions
                        .Select(c => new Caption { At = Convert.ToInt32(c.Offset.TotalSeconds), Text = c.Text })
                        // Sanitize captions, making sure cached captions as well as downloaded are cleaned of duplicates and ordered by time.
                        .Distinct().OrderBy(c => c.At).ToList();
                }
                catch (Exception ex)
                {
                    var cause = ex.GetBaseException();

                    if (cause is not OperationCanceledException)
                    {
                        captionTrack.ErrorMessage = cause.Message;
                        captionTrack.Error = ex.ToString();
                        errors.Add(ex);
                    }
                }

                video.CaptionTracks.Add(captionTrack);
            }
        }
        catch (Exception ex)
        {
            var cause = ex.GetBaseException();
            if (cause is not OperationCanceledException) errors.Add(ex);
        }

        if (errors.Count > 0) scope.Notify("Errors downloading caption tracks",
            message: video.CaptionTracks?.WithErrors()
                .Select(t => $"  {t.LanguageName}: {t.Url}")
                .Join(Environment.NewLine), [.. errors], video);

        await dataStore.SetAsync(Video.StorageKeyPrefix + video.Id, video);
    }

    /// <summary>Returns a video lookup that used the local <paramref name="videos"/> collection for better performance.</summary>
    private static Func<string, CancellationToken, Task<Video>> CreateVideoLookup(IEnumerable<Video> videos)
        => (videoId, _cancellation) => Task.FromResult(videos.Single(v => v.Id == videoId));

    /// <summary>Returns a curried <see cref="GetVideoAsync(string, CancellationToken, CommandScope, bool)"/>
    /// with the <paramref name="scope"/> supplied.</summary>
    private Func<string, CancellationToken, Task<Video>> CreateVideoLookup(CommandScope scope)
        => (videoId, cancellation) => GetVideoAsync(videoId, cancellation, scope);

    private static string SelectUrl(IReadOnlyList<Thumbnail> thumbnails) => thumbnails.MinBy(tn => tn.Resolution.Area)!.Url;
}