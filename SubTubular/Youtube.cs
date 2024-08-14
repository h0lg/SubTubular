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

public sealed class Youtube(DataStore dataStore, VideoIndexRepository videoIndexRepo)
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

    public async IAsyncEnumerable<VideoSearchResult> SearchAsync(SearchCommand command, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        List<IAsyncEnumerable<VideoSearchResult>> searches = [];
        SearchPlaylistLikeScopes(command.Channels);
        SearchPlaylistLikeScopes(command.Playlists);
        if (command.Videos?.IsValid == true) searches.Add(SearchVideosAsync(command, cancellation));
        await foreach (var result in searches.Parallelize(cancellation)) yield return result;

        void SearchPlaylistLikeScopes(PlaylistLikeScope[]? scopes)
        {
            if (scopes.HasAny()) searches.AddRange(scopes!.GetValid().DistinctBy(c => c.SingleValidated.Id)
                .Select(scope => SearchPlaylistAsync(command, scope, cancellation)));
        }
    }

    /// <summary>Searches videos defined by a playlist.</summary>
    /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
    /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
    private async IAsyncEnumerable<VideoSearchResult> SearchPlaylistAsync(SearchCommand command, PlaylistLikeScope scope,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var storageKey = scope.StorageKey;
        var index = await videoIndexRepo.GetAsync(storageKey);
        if (index == null) index = videoIndexRepo.Build(storageKey);
        var playlist = await RefreshPlaylistAsync(scope, cancellation);
        var videoIds = playlist.GetVideoIds().Take(scope.Take).ToArray();
        scope.QueueVideos(videoIds);
        var indexedVideoIds = index.GetIndexed(videoIds);

        List<IAsyncEnumerable<VideoSearchResult>> searches = [];

        if (indexedVideoIds.Length != 0)
        {
            var indexedVideoInfos = indexedVideoIds.ToDictionary(id => id, playlist.GetVideoUploaded);

            // search already indexed videos in one go - but on background task to start downloading and indexing videos in parallel
            searches.Add(SearchIndexedVideos());

            async IAsyncEnumerable<VideoSearchResult> SearchIndexedVideos()
            {
                foreach (var videoId in indexedVideoIds) scope.Report(videoId, VideoList.Status.searching);

                await foreach (var result in index.SearchAsync(command, CreateVideoLookup(scope), indexedVideoInfos, UpdatePlaylistVideosUploaded, cancellation))
                    yield return result;

                foreach (var videoId in indexedVideoIds) scope.Report(videoId, VideoList.Status.searched);
            }
        }

        var unIndexedVideoIds = videoIds.Except(indexedVideoIds).ToArray();

        // load, index and search not yet indexed videos
        if (unIndexedVideoIds.Length > 0) searches.Add(SearchUnindexedVideos(command,
            unIndexedVideoIds, index, scope, cancellation, UpdatePlaylistVideosUploaded));

        scope.Report(VideoList.Status.searching);

        await foreach (var result in searches.Parallelize(cancellation))
        {
            result.Scope = scope;
            yield return result;
        }

        scope.Report(VideoList.Status.searched);
        index.Dispose();

        async Task UpdatePlaylistVideosUploaded(IEnumerable<Video> videos)
        {
            var updated = false;

            foreach (var video in videos)
            {
                if (playlist.SetUploaded(video)) updated = true;
            }

            if (updated) await dataStore.SetAsync(storageKey, playlist);
        }
    }

    internal Task<Playlist?> GetPlaylistAsync(PlaylistScope scope, CancellationToken cancellation) =>
        GetPlaylistAsync(scope, async () =>
        {
            var playlist = await Client.Playlists.GetAsync(scope.SingleValidated.Id, cancellation);
            return (playlist.Title, SelectUrl(playlist.Thumbnails), playlist.Author?.ChannelTitle);
        });

    internal Task<Playlist?> GetPlaylistAsync(ChannelScope scope, CancellationToken cancellation) =>
        GetPlaylistAsync(scope, async () =>
        {
            var channel = await Client.Channels.GetAsync(scope.SingleValidated.Id, cancellation);
            return (channel.Title, SelectUrl(channel.Thumbnails), null);
        });

    private async Task<Playlist?> GetPlaylistAsync(PlaylistLikeScope scope,
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

    private async Task<Playlist> RefreshPlaylistAsync(PlaylistLikeScope scope, CancellationToken cancellation)
    {
        var playlist = scope.SingleValidated.Playlist!;

        // return fresh playlist with sufficient videos loaded
        if (DateTime.UtcNow.AddHours(-Math.Abs(scope.CacheHours)) <= playlist.Loaded
            && scope.Take <= playlist.GetVideoIds().Count()) return playlist;

        // playlist cache is outdated or lacking sufficient videos
        scope.Report(VideoList.Status.refreshing);

        try
        {
            // load and update videos in playlist while keeping existing video info
            var freshVideos = await GetVideos(scope, cancellation).CollectAsync(scope.Take);
            playlist.Loaded = DateTime.UtcNow;
            playlist.AddVideoIds(freshVideos.Select(v => v.Id.Value).ToArray());
            await dataStore.SetAsync(scope.StorageKey, playlist);
        }
        /*  treat playlist identified by user input not being available as input error
            and re-throw otherwise; the uploads playlist of a channel being unavailable is unexpected */
        catch (PlaylistUnavailableException ex) when (scope is PlaylistScope playlistScope)
        { throw new InputException($"Could not find {playlistScope.Describe().Join(" ")}.", ex); }

        return playlist;

        IAsyncEnumerable<PlaylistVideo> GetVideos(PlaylistLikeScope scope, CancellationToken cancellation) => scope switch
        {
            ChannelScope searchChannel => Client.Channels.GetUploadsAsync(searchChannel.SingleValidated.Id, cancellation),
            PlaylistScope searchPlaylist => Client.Playlists.GetVideosAsync(searchPlaylist.Alias, cancellation),
            _ => throw new NotImplementedException($"Getting videos for the {scope.GetType()} is not implemented.")
        };
    }

    private async IAsyncEnumerable<VideoSearchResult> SearchUnindexedVideos(SearchCommand command,
        string[] unIndexedVideoIds, VideoIndex index, CommandScope scope,
        [EnumeratorCancellation] CancellationToken cancellation,
        Func<IEnumerable<Video>, Task>? updatePlaylistVideosUploaded = default)
    {
        cancellation.ThrowIfCancellationRequested();
        scope.Report(VideoList.Status.indexingAndSearching);

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

                    Video? video = command.Videos?.Validated.SingleOrDefault(v => v.Id == id)?.Video;
                    video ??= await GetVideoAsync(id, cancellation, scope, downloadCaptionTracksAndSave: false);
                    if (video.CaptionTracks.Count == 0) await DownloadCaptionTracksAndSaveAsync(video, cancellation);

                    await unIndexedVideos.Writer.WriteAsync(video);
                    scope.Report(id, VideoList.Status.indexing);
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

        // local getter reusing already loaded video from uncommitted bag for better performance
        Func<string, CancellationToken, Task<Video>> getVideoAsync = CreateVideoLookup(uncommitted);

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
    /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
    /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
    private async IAsyncEnumerable<VideoSearchResult> SearchVideosAsync(SearchCommand command, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        VideosScope scope = command.Videos!;
        var videoIds = scope.GetValidatedIds();
        scope.QueueVideos(videoIds);
        var storageKey = Video.StorageKeyPrefix + videoIds.Order().Join(" ");
        var index = await videoIndexRepo.GetAsync(storageKey);

        if (index == null)
        {
            index = videoIndexRepo.Build(storageKey);

            await foreach (var result in SearchUnindexedVideos(command, videoIds, index, command.Videos!, cancellation))
                yield return result;
        }
        else
        {
            command.Videos!.Report(VideoList.Status.searching);

            // indexed videos are assumed to have downloaded their caption tracks already
            Video[] videos = command.Videos!.Validated.Select(v => v.Video!).ToArray();

            await foreach (var result in index.SearchAsync(command, CreateVideoLookup(videos), cancellation: cancellation))
                yield return result;
        }

        command.Videos!.Report(VideoList.Status.searched);
        index.Dispose();
    }

    /// <summary>Returns the <see cref="Video.Keywords"/> and their corresponding number of occurrences
    /// from the videos scoped by <paramref name="command"/>.</summary>
    public async IAsyncEnumerable<(string keyword, string videoId, CommandScope scope)> ListKeywordsAsync(ListKeywords command,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var channel = Pipe.CreateBounded<(string keyword, string videoId, CommandScope scope)>(
            new BoundedChannelOptions(5) { SingleReader = true });

        async Task GetKeywords(string videoId, CommandScope scope)
        {
            var video = await GetVideoAsync(videoId, cancellation, scope);
            scope.Report(videoId, VideoList.Status.searching);

            foreach (var keyword in video.Keywords)
                await channel.Writer.WriteAsync((keyword, videoId, scope));

            scope.Report(videoId, VideoList.Status.searched);
        }

        var lookupTasks = command.GetPlaylistLikeScopes().GetValid().Select(scope =>
            Task.Run(async () =>
            {
                var playlist = await RefreshPlaylistAsync(scope, cancellation);
                var videoIds = playlist.GetVideoIds().Take(scope.Take).ToArray();
                scope.QueueVideos(videoIds);
                scope.Report(VideoList.Status.searching);
                foreach (var videoId in videoIds) await GetKeywords(videoId, scope);
                scope.Report(VideoList.Status.searched);
            }, cancellation))
            .ToList();

        if (command.Videos?.IsValid == true)
        {
            IEnumerable<string> videoIds = command.Videos.GetValidatedIds();
            command.Videos.QueueVideos(videoIds);

            lookupTasks.Add(Task.Run(async () =>
            {
                command.Videos.Report(VideoList.Status.searching);

                foreach (var videoId in videoIds)
                    await GetKeywords(videoId, command.Videos);

                command.Videos.Report(VideoList.Status.searched);
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

    public static void AggregateKeywords(string keyword, string videoId, CommandScope scope,
        Dictionary<CommandScope, Dictionary<string, List<string>>> keywordsByScope)
    {
        if (keywordsByScope.TryGetValue(scope, out Dictionary<string, List<string>>? videoIdsByKeyword))
        {
            if (videoIdsByKeyword.TryGetValue(keyword, out List<string>? videoIds)) videoIds.Add(videoId);
            else videoIdsByKeyword.Add(keyword, [videoId]);
        }
        else keywordsByScope.Add(scope, new Dictionary<string, List<string>> { { keyword, [videoId] } });
    }

    public static IOrderedEnumerable<KeyValuePair<string, List<string>>> OrderKeywords(Dictionary<string, List<string>> keywordsByScope)
        => keywordsByScope.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key);

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

            try
            {
                var vid = await Client.Videos.GetAsync(videoId, cancellation);
                video = MapVideo(vid);
                video.UnIndexed = true; // to re-index it if it was already indexed
                if (downloadCaptionTracksAndSave) await DownloadCaptionTracksAndSaveAsync(video, cancellation);
            }
            catch (HttpRequestException ex) when (ex.IsNotFound())
            { throw new InputException($"Video '{videoId}' could not be found.", ex); }
        }

        return video;
    }

    private async Task DownloadCaptionTracksAndSaveAsync(Video video, CancellationToken cancellation)
    {
        await foreach (var track in DownloadCaptionTracksAsync(video.Id, cancellation))
            video.CaptionTracks.Add(track);

        await dataStore.SetAsync(Video.StorageKeyPrefix + video.Id, video);
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
                    // Sanitize captions, making sure cached captions as well as downloaded are cleaned of duplicates and ordered by time.
                    .Distinct().OrderBy(c => c.At).ToList();
            }
            catch (Exception ex)
            {
                captionTrack.ErrorMessage = ex.Message;
                captionTrack.Error = ex.ToString();
            }

            yield return captionTrack;
        }
    }

    /// <summary>Returns a video lookup that used the local <paramref name="videos"/> collection for better performance.</summary>
    private static Func<string, CancellationToken, Task<Video>> CreateVideoLookup(IEnumerable<Video> videos)
        => (videoId, _cancellation) => Task.FromResult(videos.Single(v => v.Id == videoId));

    /// <summary>Returns a curried <see cref="GetVideoAsync(string, CancellationToken, CommandScope, bool)"/>
    /// with the <paramref name="scope"/> supplied.</summary>
    private Func<string, CancellationToken, Task<Video>> CreateVideoLookup(CommandScope scope)
        => (videoId, cancellation) => GetVideoAsync(videoId, cancellation, scope);

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

    private static string SelectUrl(IReadOnlyList<Thumbnail> thumbnails) => thumbnails.MinBy(tn => tn.Resolution.Area)!.Url;
}