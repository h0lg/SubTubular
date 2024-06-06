using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SubTubular.Extensions;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Playlists;
using Pipe = System.Threading.Channels.Channel; // to avoid conflict with YoutubeExplode.Channels.Channel

namespace SubTubular;

public sealed class Youtube
{
    public static string GetVideoUrl(string videoId) => "https://youtu.be/" + videoId;

    internal static string GetChannelUrl(object alias)
    {
        var urlGlue = alias is ChannelHandle ? "@" : alias is ChannelSlug ? "c/"
            : alias is UserName ? "user/" : alias is ChannelId ? "channel/"
            : throw new NotImplementedException($"Generating URL for channel alias {alias.GetType()} is not implemented.");

        return $"https://www.youtube.com/{urlGlue}{alias}";
    }

    public readonly YoutubeClient Client = new();
    private readonly DataStore dataStore;
    private readonly VideoIndexRepository videoIndexRepo;

    public Youtube(DataStore dataStore, VideoIndexRepository videoIndexRepo)
    {
        this.dataStore = dataStore;
        this.videoIndexRepo = videoIndexRepo;
    }

    public async IAsyncEnumerable<VideoSearchResult> SearchAsync(SearchCommand command,
        IProgress<BatchProgress>? progressReporter = default, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        BatchProgressReporter? progress = CreateBatchProgress(command, progressReporter);
        List<IAsyncEnumerable<VideoSearchResult>> searches = [];

        if (command.Channels.HasAny()) searches.AddRange(command.Channels!.GetValid()
            .Select(channel => SearchPlaylistAsync(command, channel, progress?.CreateVideoListProgress(channel), cancellation)));

        if (command.Playlists.HasAny()) searches.AddRange(command.Playlists!.GetValid()
            .Select(playlist => SearchPlaylistAsync(command, playlist, progress?.CreateVideoListProgress(playlist), cancellation)));

        if (command.Videos?.IsValid == true) searches.Add(SearchVideosAsync(command, progress?.CreateVideoListProgress(command.Videos), cancellation));

        await foreach (var result in searches.Parallelize(cancellation)) yield return result;
    }

    /// <summary>Searches videos defined by a playlist.</summary>
    /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
    /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
    private async IAsyncEnumerable<VideoSearchResult> SearchPlaylistAsync(SearchCommand command, PlaylistLikeScope scope,
        BatchProgressReporter.VideoListProgress? progress, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var storageKey = scope.StorageKey;
        var index = await videoIndexRepo.GetAsync(storageKey);
        if (index == null) index = videoIndexRepo.Build(storageKey);
        var playlist = await RefreshPlaylistAsync(scope, cancellation, progress);
        var videoIds = playlist.Videos.Keys.Take(scope.Top).ToArray();
        progress?.SetVideos(videoIds);
        var indexedVideoIds = index.GetIndexed(videoIds);

        List<IAsyncEnumerable<VideoSearchResult>> searches = [];

        if (indexedVideoIds.Length != 0)
        {
            var indexedVideoInfos = indexedVideoIds.ToDictionary(id => id, id => playlist.Videos[id]);

            // search already indexed videos in one go - but on background task to start downloading and indexing videos in parallel
            searches.Add(index.SearchAsync(command, CreateVideoLookup(progress), indexedVideoInfos, UpdatePlaylistVideosUploaded, cancellation));
            progress?.Report(BatchProgress.Status.searching);
        }

        var unIndexedVideoIds = videoIds.Except(indexedVideoIds).ToArray();

        // load, index and search not yet indexed videos
        if (unIndexedVideoIds.Length > 0) searches.Add(SearchUnindexedVideos(command,
            unIndexedVideoIds, index, progress, cancellation, UpdatePlaylistVideosUploaded));

        await foreach (var result in searches.Parallelize(cancellation)) yield return result;

        async Task UpdatePlaylistVideosUploaded(IEnumerable<Video> videos)
        {
            var updated = false;

            foreach (var video in videos)
            {
                if (playlist.Videos[video.Id] != video.Uploaded)
                {
                    playlist.Videos[video.Id] = video.Uploaded;
                    updated = true;
                }
            }

            if (updated) await dataStore.SetAsync(storageKey, playlist);
        }
    }

    private async Task<Playlist> RefreshPlaylistAsync(PlaylistLikeScope command,
        CancellationToken cancellation, BatchProgressReporter.VideoListProgress? progress)
    {
        var storageKey = command.StorageKey;
        var playlist = await dataStore.GetAsync<Playlist>(storageKey); //get cached

        if (playlist == null //playlist cache is missing, outdated or lacking sufficient videos
            || playlist.Loaded < DateTime.UtcNow.AddHours(-Math.Abs(command.CacheHours))
            || playlist.Videos.Count < command.Top)
        {
            progress?.Report(BatchProgress.Status.downloading);
            if (playlist == null) playlist = new Playlist();

            try
            {
                // load and update videos in playlist while keeping existing video info
                var freshVideos = await GetVideosAsync(command, cancellation).CollectAsync(command.Top);
                playlist.Loaded = DateTime.UtcNow;

                // use new order but append older entries; note that this leaves remotely deleted videos in the playlist
                var freshKeys = freshVideos.Select(v => v.Id.Value).ToArray();

                playlist.Videos = freshKeys.Concat(playlist.Videos.Keys.Except(freshKeys))
                    .ToDictionary(id => id, id => playlist.Videos.TryGetValue(id, out var uploaded) ? uploaded : null);

                await dataStore.SetAsync(storageKey, playlist);
            }
            /*  treat playlist identified by user input not being available as input error
                and re-throw otherwise; the uploads playlist of a channel being unavailable is unexpected */
            catch (PlaylistUnavailableException ex) when (command is PlaylistScope searchPlaylist)
            { throw new InputException($"Could not find {searchPlaylist.Describe()}.", ex); }
        }

        return playlist;
    }

    private IAsyncEnumerable<PlaylistVideo> GetVideosAsync(PlaylistLikeScope scope, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (scope is ChannelScope searchChannel) return Client.Channels.GetUploadsAsync(searchChannel.ValidId!, cancellation);
        if (scope is PlaylistScope searchPlaylist) return Client.Playlists.GetVideosAsync(searchPlaylist.Alias, cancellation);
        throw new NotImplementedException($"Getting videos for the {scope.GetType()} is not implemented.");
    }

    private async IAsyncEnumerable<VideoSearchResult> SearchUnindexedVideos(SearchCommand command,
        string[] unIndexedVideoIds, VideoIndex index,
        BatchProgressReporter.VideoListProgress? progress,
        [EnumeratorCancellation] CancellationToken cancellation,
        Func<IEnumerable<Video>, Task>? updatePlaylistVideosUploaded = default)
    {
        cancellation.ThrowIfCancellationRequested();
        progress?.Report(BatchProgress.Status.indexingAndSearching);

        /* limit channel capacity to avoid holding a lot of loaded but unprocessed videos in memory
            SingleReader because we're reading from it synchronously */
        var unIndexedVideos = Pipe.CreateBounded<Video>(new BoundedChannelOptions(5) { SingleReader = true });

        // load videos asynchronously in the background and put them on the unIndexedVideos channel for processing
        var loadVideos = Task.Run(async () =>
        {
            var loadLimiter = new SemaphoreSlim(5, 5);

            var downloads = unIndexedVideoIds.Select(id => Task.Run(async () =>
            {
                /*  pause task here before starting download until channel accepts another video
                    to avoid holding a lot of loaded but unprocessed videos in memory */
                await loadLimiter.WaitAsync();

                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    var video = await GetVideoAsync(id, cancellation, progress);
                    await unIndexedVideos.Writer.WriteAsync(video);
                    progress?.Report(id, BatchProgress.Status.indexing);
                }
                /* only start another download if channel has accepted the video or an error occurred */
                finally { loadLimiter.Release(); }
            })).ToArray();

            try { await Task.WhenAll(downloads).WithAggregateException(); }
            finally
            {
                // complete writing after all download tasks finished
                unIndexedVideos.Writer.Complete();
            }
        });

        var uncommitted = new List<Video>(); // batch of loaded and indexed, but uncommitted video index changes

        // local getter preferring to reuse already loaded video from uncommitted bag for better performance
        Func<string, CancellationToken, Task<Video>> getVideoAsync = async (videoId, cancellation)
            => uncommitted.SingleOrDefault(v => v.Id == videoId) ?? await GetVideoAsync(videoId, cancellation, progress);

        // read synchronously from the channel because we're writing to the same video index
        await foreach (var video in unIndexedVideos.Reader.ReadAllAsync())
        {
            cancellation.ThrowIfCancellationRequested();
            if (uncommitted.Count == 0) index.BeginBatchChange();
            await index.AddAsync(video, cancellation);
            uncommitted.Add(video);

            // save batch of changes
            if (uncommitted.Count >= 5 // to prevent the batch from growing too big
                || unIndexedVideos.Reader.Completion.IsCompleted // to save remaining changes
                || unIndexedVideos.Reader.Count == 0) // to use resources efficiently while we've got nothing queued up for indexing
            {
                List<Task> saveJobs = [index.CommitBatchChangeAsync()];
                if (updatePlaylistVideosUploaded != null) saveJobs.Add(updatePlaylistVideosUploaded(uncommitted));
                await Task.WhenAll(saveJobs).WithAggregateException();

                var indexedVideoInfos = uncommitted.ToDictionary(v => v.Id, v => v.Uploaded as DateTime?);
                progress?.Report(uncommitted, BatchProgress.Status.searching);

                // search after committing index changes to output matches as we go
                await foreach (var result in index.SearchAsync(command, getVideoAsync, indexedVideoInfos, cancellation: cancellation))
                    yield return result;

                progress?.Report(uncommitted, BatchProgress.Status.searched);
                uncommitted.Clear(); // safe to do because we're reading synchronously and no other thread could have added to it in between
            }
        }

        await loadVideos; // just to re-throw possible exceptions; should have completed at this point
    }

    /// <summary>Searches videos scoped by the specified <paramref name="command"/>.</summary>
    /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
    /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
    private async IAsyncEnumerable<VideoSearchResult> SearchVideosAsync(SearchCommand command,
        BatchProgressReporter.VideoListProgress? progress, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var videoIds = command.Videos!.ValidIds!;
        progress?.SetVideos(videoIds);
        var storageKey = Video.StorageKeyPrefix + videoIds.Order().Join(" ");
        var index = await videoIndexRepo.GetAsync(storageKey);

        if (index == null)
        {
            index = videoIndexRepo.Build(storageKey);

            await foreach (var result in SearchUnindexedVideos(command, videoIds, index, progress, cancellation))
                yield return result;
        }
        else
        {
            progress?.Report(BatchProgress.Status.searching);

            await foreach (var result in index.SearchAsync(command, CreateVideoLookup(progress), cancellation: cancellation))
                yield return result;
        }

        progress?.Report(BatchProgress.Status.searched);
    }

    /// <summary>Returns the <see cref="Video.Keywords"/> and their corresponding number of occurrences
    /// from the videos scoped by <paramref name="command"/>.</summary>
    public async IAsyncEnumerable<(string keyword, string videoId, CommandScope scope)> ListKeywordsAsync(ListKeywords command,
        IProgress<BatchProgress>? progressReporter = default,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        BatchProgressReporter? progress = CreateBatchProgress(command, progressReporter);

        var channel = Pipe.CreateBounded<(string keyword, string videoId, CommandScope scope)>(
            new BoundedChannelOptions(5) { SingleReader = true });

        async Task GetKeywords(string videoId, CommandScope scope, BatchProgressReporter.VideoListProgress? listProgress)
        {
            var video = await GetVideoAsync(videoId, cancellation, listProgress);
            listProgress?.Report(videoId, BatchProgress.Status.searching);

            foreach (var keyword in video.Keywords)
                await channel.Writer.WriteAsync((keyword, videoId, scope));

            listProgress?.Report(videoId, BatchProgress.Status.searched);
        }

        var lookupTasks = command.GetPlaylistLikeScopes().GetValid().Select(scope =>
            Task.Run(async () =>
            {
                var listProgress = progress?.CreateVideoListProgress(scope);
                var playlist = await RefreshPlaylistAsync(scope, cancellation, listProgress);
                var videoIds = playlist.Videos.Keys.Take(scope.Top).ToArray();
                listProgress?.SetVideos(videoIds);
                listProgress?.Report(BatchProgress.Status.searching);
                foreach (var videoId in videoIds) await GetKeywords(videoId, scope, listProgress);
                listProgress?.Report(BatchProgress.Status.searched);
            }, cancellation))
            .ToList();

        if (command.Videos?.IsValid == true)
        {
            var listProgress = progress?.CreateVideoListProgress(command.Videos);
            listProgress?.SetVideos(command.Videos.ValidIds!);

            lookupTasks.Add(Task.Run(async () =>
            {
                listProgress?.Report(BatchProgress.Status.searching);

                foreach (var videoId in command.Videos.ValidIds!)
                    await GetKeywords(videoId, command.Videos, listProgress);

                listProgress?.Report(BatchProgress.Status.searched);
            }, cancellation));
        }

        // hook up writer completion before starting to read to ensure the reader knows when it's done
        var lookups = Task.WhenAll(lookupTasks).ContinueWith(t =>
        {
            channel.Writer.Complete();
            //if (t.Exception != null) throw t.Exception;
        }).WithAggregateException();

        // start reading
        await foreach (var keyword in channel.Reader.ReadAllAsync(cancellation)) yield return keyword;

        await lookups; // to propagate exceptions
    }

    private async Task<Video> GetVideoAsync(string videoId, CancellationToken cancellation, BatchProgressReporter.VideoListProgress? progress)
    {
        cancellation.ThrowIfCancellationRequested();
        var storageKey = Video.StorageKeyPrefix + videoId;
        var video = await dataStore.GetAsync<Video>(storageKey);

        if (video == null)
        {
            progress?.Report(videoId, BatchProgress.Status.downloading);

            try
            {
                var vid = await Client.Videos.GetAsync(videoId, cancellation);
                video = MapVideo(vid);
                video.UnIndexed = true; // to re-index it if it was already indexed

                await foreach (var track in DownloadCaptionTracksAsync(videoId, cancellation))
                    video.CaptionTracks.Add(track);

                await dataStore.SetAsync(storageKey, video);
            }
            catch (HttpRequestException ex) when (ex.IsNotFound())
            { throw new InputException($"Video '{videoId}' could not be found.", ex); }
        }

        return video;
    }

    private static Video MapVideo(YoutubeExplode.Videos.Video video) => new()
    {
        Id = video.Id.Value,
        Title = video.Title,
        Description = video.Description,
        Keywords = video.Keywords.ToArray(),
        Uploaded = video.UploadDate.UtcDateTime
    };

    private async IAsyncEnumerable<CaptionTrack> DownloadCaptionTracksAsync(string videoId,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var trackManifest = await Client.Videos.ClosedCaptions.GetManifestAsync(videoId, cancellation);

        foreach (var trackInfo in trackManifest.Tracks)
        {
            cancellation.ThrowIfCancellationRequested();
            var captionTrack = new CaptionTrack { LanguageName = trackInfo.Language.Name, Url = trackInfo.Url };

            try
            {
                // Get the actual closed caption track
                var track = await Client.Videos.ClosedCaptions.GetAsync(trackInfo, cancellation);

                captionTrack.Captions = track.Captions
                    .Select(c => new Caption { At = Convert.ToInt32(c.Offset.TotalSeconds), Text = c.Text })
                    /* Sanitize captions, making sure cached captions as well as downloaded
                        are cleaned of duplicates and ordered by time. */
                    .Distinct().OrderBy(c => c.At)
                    .ToList();
            }
            catch (Exception ex)
            {
                captionTrack.ErrorMessage = ex.Message;
                captionTrack.Error = ex.ToString();
            }

            yield return captionTrack;
        }
    }

    private static BatchProgressReporter? CreateBatchProgress(OutputCommand command, IProgress<BatchProgress>? progressReporter)
    {
        if (progressReporter == default) return default;
        var playlists = command.GetValidScopes().ToDictionary(scope => scope, _ => new BatchProgress.VideoList());
        return new(progressReporter!, new BatchProgress() { VideoLists = playlists });
    }

    /// <summary>Returns a curried <see cref="GetVideoAsync(string, CancellationToken, BatchProgressReporter.VideoListProgress?)"/>
    /// with the <paramref name="progress"/> supplied.</summary>
    private Func<string, CancellationToken, Task<Video>> CreateVideoLookup(BatchProgressReporter.VideoListProgress? progress)
        => (videoId, cancellation) => GetVideoAsync(videoId, cancellation, progress);
}
