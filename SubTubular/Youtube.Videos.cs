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
        if (token.IsCancellationRequested) yield break;
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

                    playlist?.Update(video);

                    await unIndexedVideos.Writer.WriteAsync(video);
                    scope.Report(id, VideoList.Status.indexing);
                }
                /* only start another download if channel has accepted the video or an error occurred */
                finally { loadLimiter.Release(); }
            }, token));

            await Task.WhenAll(downloads).WithAggregateException()
               .ContinueWith(t =>
               {
                   // complete writing after all download tasks finished
                   unIndexedVideos.Writer.Complete();
                   if (t.Exception != null) throw t.Exception;
               }, token);
        });

        var uncommitted = new List<Video>(); // batch of loaded and indexed, but uncommitted video index changes

        // local getter reusing already loaded video from uncommitted bag for better performance
        Func<string, CancellationToken, Task<Video>> getVideoAsync = CreateVideoLookup(uncommitted);

        // read synchronously from the channel because we're writing to the same video index
        await foreach (var video in unIndexedVideos.Reader.ReadAllAsync())
        {
            if (token.IsCancellationRequested) break;
            if (uncommitted.Count == 0) index.BeginBatchChange();
            await index.AddOrUpdateAsync(video, token);
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
                await foreach (var result in index.SearchAsync(command, getVideoAsync, indexedVideoInfos, token: token))
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
        if (token.IsCancellationRequested) return;
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
                await foreach (var result in SearchUnindexedVideos(command, videoIds, index, scope, token))
                    await yieldResult(result);
            }, token);
        }
        else searching = Task.Run(async () =>
        {
            scope.Report(VideoList.Status.searching);

            // indexed videos are assumed to have downloaded their caption tracks already
            Video[] videos = scope.Validated.Select(v => v.Video!).ToArray();

            await foreach (var result in index.SearchAsync(command, CreateVideoLookup(videos), token: token))
                await yieldResult(result);
        }, token);

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

            var vid = await Client.Videos.GetAsync(videoId, token);
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
            var trackManifest = await Client.Videos.ClosedCaptions.GetManifestAsync(video.Id, token);
            video.CaptionTracks = [];

            foreach (var trackInfo in trackManifest.Tracks)
            {
                var captionTrack = new CaptionTrack { LanguageName = trackInfo.Language.Name, Url = trackInfo.Url };

                try
                {
                    // Get the actual closed caption track
                    var track = await Client.Videos.ClosedCaptions.GetAsync(trackInfo, token);

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
        => (videoId, _) => Task.FromResult(videos.Single(v => v.Id == videoId));
}
