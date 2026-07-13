using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SubTubular.Extensions;

namespace SubTubular;

partial class Youtube
{
    public static string GetVideoUrl(string videoId) => "https://youtu.be/" + videoId;

    private async IAsyncEnumerable<VideoSearchResult> SearchUnindexedVideos(SearchCommand command,
        string[] unIndexedVideoIds, VideoIndex index, CommandScope scope,
        [EnumeratorCancellation] CancellationToken token,
        Playlist? playlist = default)
    {
        token.ThrowIfCancellationRequested(); // for SearchUpdatingScope
        scope.Report(VideoList.Status.indexingAndSearching);

        /* limit channel capacity to avoid holding a lot of loaded but unprocessed videos in memory
            SingleReader because we're reading from it synchronously */
        const int queueSize = 10;
        var unIndexedVideos = Channel.CreateBounded<Video>(new BoundedChannelOptions(queueSize) { SingleReader = true });

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
                    video ??= await GetVideoAsync(id, token, scope, downloadCaptionTracksAndSave: false);

                    // re/download caption tracks for the video
                    if (!video.GetCaptionTrackDownloadStatus().IsComplete())
                        await DownloadCaptionTracksAndSaveAsync(video, scope, token);

                    token.ThrowIfCancellationRequested();
                    playlist?.Update(video);

                    await unIndexedVideos.Writer.WriteAsync(video, token);
                }
                catch (Exception ex)
                {
                    // notify scope immediately about errors that need reporting to record their time correctly via the notification
                    if (ex.NeedsReporting()) scope.Notify("Error loading video " + id, errors: [ex]);
                    else throw; // bubble less important errors up to have them collected by SearchUpdatingScope
                }
                // only start another download if channel has accepted the video or an error occurred
                finally { loadLimiter.Release(); }
            }, token));

            try { await Task.WhenAll(downloads).WithAggregateException(); }
            finally
            {
                // complete writing after all download tasks finished
                unIndexedVideos.Writer.Complete();
            }
        });

        var uncommitted = new List<Video>(); // batch of loaded and indexed, but uncommitted video index changes

        // local lookup reusing already loaded video from uncommitted bag for better performance; can be used because videos in it have caption tracks loaded
        Task<Video> LookupVideoLocally(string videoId, CancellationToken _) => Task.FromResult(uncommitted.Single(v => v.Id == videoId));

        // read synchronously from the channel because we're writing to the same video index
        // don't pass cancellation token to avoid throwing before loadVideos is awaited below
        await foreach (var video in unIndexedVideos.Reader.ReadAllAsync())
        {
            if (token.IsCancellationRequested) break; // end loop gracefully to throw below
            if (uncommitted.Count == 0) index.BeginBatchChange();
            await index.AddOrUpdateAsync(video, scope, token);
            uncommitted.Add(video);

            // save batch of changes
            if (uncommitted.Count >= queueSize // to prevent the batch from growing too big
                || unIndexedVideos.Reader.Completion.IsCompleted // to save remaining changes
                || unIndexedVideos.Reader.Count == 0) // to use resources efficiently while we've got nothing queued up for indexing
            {
                await index.CommitBatchChangeAsync(token);

                var indexedVideoInfos = uncommitted.ToDictionary(v => v.Id, v => v.Uploaded as DateTime?);
                scope.Report(uncommitted, VideoList.Status.searching);

                // search after committing index changes to output matches as we go
                await foreach (var result in index.SearchAsync(command, scope, LookupVideoLocally, indexedVideoInfos, token: token))
                    yield return result;

                scope.Report(uncommitted, VideoList.Status.searched);
                uncommitted.Clear(); // safe to do because we're reading synchronously and no other thread could have added to it in between
            }
        }

        await loadVideos; // just to re-throw possible exceptions; should have completed at this point
    }

    /// <summary>Searches videos scoped by the specified <paramref name="command"/>.</summary>
    private async Task SearchVideosAsync(SearchCommand command,
        Func<VideoSearchResult, ValueTask> yieldResult, CancellationToken token)
    {
        VideosScope scope = command.Videos!;

        if (token.IsCancellationRequested)
        {
            scope.Report(VideoList.Status.canceled); // because SearchUpdatingScope won't get the chance
            return;
        }

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
                await foreach (var result in SearchUnindexedVideos(command, videoIds, index, scope, token))
                    await yieldResult(result);
            }, token);
        }
        else searching = Task.Run(async () =>
        {
            /* Validated video may have downloaded caption tracks already when they were indexed,
             * but we can't rely on it because validation doesn't do it
             * and the video caches that were once indexed may have been deleted separately */
            var videosById = scope.Validated.Select(v => v.Video!).ToDictionary(v => v.Id);

            scope.Report(VideoList.Status.searching);
            scope.Report(videosById.Values, VideoList.Status.searching);

            await foreach (var result in index.SearchAsync(command, scope, LookupVideoLocallyFirst, token: token))
                await yieldResult(result);

            scope.Report(videosById.Values, VideoList.Status.searched);

            async Task<Video> LookupVideoLocallyFirst(string videoId, CancellationToken token)
                // prefer lookup from local collection because it's faster - but only if the video found has its caption tracks downloaded
                => videosById.TryGetValue(videoId, out var video) && video.GetCaptionTrackDownloadStatus().IsComplete() ? video
                    : await GetVideoAsync(videoId, token, scope); // otherwise look it up remotely, downloading the caption tracks
        }, token);

        await SearchUpdatingScope(searching, scope, () => index.Dispose());
    }

    internal async Task<Video> GetVideoAsync(string videoId, CancellationToken token,
        CommandScope scope, bool downloadCaptionTracksAndSave = true)
    {
        token.ThrowIfCancellationRequested();
        var storageKey = Video.StorageKeyPrefix + videoId;
        scope.Report(videoId, VideoList.Status.loading);
        var video = await dataStore.GetAsync<Video>(storageKey);

        if (video == null)
        {
            scope.Report(videoId, VideoList.Status.downloading);
            var vid = await client.Videos.GetAsync(videoId, token);
            scope.Report(videoId, VideoList.Status.validated);
            video = MapVideo(vid);
            video.UnIndexed = true; // to re-index it if it was already indexed
            if (downloadCaptionTracksAndSave) await DownloadCaptionTracksAndSaveAsync(video, scope, token);
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

    private async Task DownloadCaptionTracksAndSaveAsync(Video video, CommandScope scope, CancellationToken token)
    {
        List<Exception> errors = [];

        try
        {
            var trackManifest = await client.Videos.ClosedCaptions.GetManifestAsync(video.Id, token);
            video.CaptionTracks = [];

            foreach (var trackInfo in trackManifest.Tracks)
            {
                var captionTrack = new CaptionTrack { LanguageName = trackInfo.Language.Name, Url = trackInfo.Url };

                try
                {
                    // Get the actual closed caption track
                    var track = await client.Videos.ClosedCaptions.GetAsync(trackInfo, token);

                    captionTrack.Captions = [.. track.Captions
                        .Select(c => new Caption { At = Convert.ToInt32(c.Offset.TotalSeconds), Text = c.Text })
                        // Sanitize captions, making sure cached captions as well as downloaded are cleaned of duplicates and ordered by time.
                        .Distinct().OrderBy(c => c.At)];
                }
                catch (Exception ex)
                {
                    if (ex is not OperationCanceledException)
                    {
                        captionTrack.ErrorMessage = ex.Message;
                        captionTrack.Error = ex.ToString();
                        errors.Add(ex);
                    }
                }

                video.CaptionTracks.Add(captionTrack);
            }
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException) errors.Add(ex);
        }

        if (errors.Count > 0) scope.Notify("Errors downloading caption tracks",
            message: video.CaptionTracks?.WithErrors()
                .Select(t => $"  {t.LanguageName}: {t.Url}")
                .Join(Environment.NewLine), [.. errors], video);

        await dataStore.SetAsync(Video.StorageKeyPrefix + video.Id, video);
    }
}
