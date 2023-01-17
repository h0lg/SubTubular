using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;

namespace SubTubular
{
    internal sealed class Youtube
    {
        private readonly YoutubeClient youtube = new YoutubeClient();
        private readonly DataStore dataStore;
        private readonly VideoIndexRepository videoIndexRepo;

        internal Youtube(DataStore dataStore, VideoIndexRepository videoIndexRepo)
        {
            this.dataStore = dataStore;
            this.videoIndexRepo = videoIndexRepo;
        }

        // an adapter injecting the YoutubeClient and data store into the command
        internal Task RemoteValidateAsync(RemoteValidated command, CancellationToken cancellation)
            => command.RemoteValidateAsync(youtube, dataStore, cancellation); // inject YoutubeClient

        /// <summary>Searches videos defined by a playlist.</summary>
        /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
        /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
        internal async IAsyncEnumerable<VideoSearchResult> SearchPlaylistAsync(
            SearchPlaylistCommand command, [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var storageKey = command.StorageKey;
            var index = await videoIndexRepo.GetAsync(storageKey);
            if (index == null) index = videoIndexRepo.Build(storageKey);
            var playlist = await GetPlaylistAsync(command,  cancellation);
            var searches = new List<Task>();
            var videoResults = Channel.CreateUnbounded<VideoSearchResult>(new UnboundedChannelOptions() { SingleReader = true });
            var videoIds = playlist.Videos.Keys.Take(command.Top).ToArray();
            var indexedVideoIds = index.GetIndexed(videoIds);
            var indexedVideoInfos = indexedVideoIds.ToDictionary(id => id, id => playlist.Videos[id]);

            /*  search already indexed videos in one go - but on background task
                to start downloading and indexing videos in parallel */
            searches.Add(Task.Run(async () =>
            {
                await foreach (var result in index.SearchAsync(command, GetVideoAsync,
                    cancellation, indexedVideoInfos, UpdatePlaylistVideosUploaded))
                    await videoResults.Writer.WriteAsync(result);
            }));

            var unIndexedVideoIds = videoIds.Except(indexedVideoIds).ToArray();

            // load, index and search un-indexed videos
            if (unIndexedVideoIds.Length > 0) searches.Add(Task.Run(async () =>
            {
                // search already indexed videos in one go
                await foreach (var result in SearchUnindexedVideos(command,
                    unIndexedVideoIds, index, cancellation, UpdatePlaylistVideosUploaded))
                    await videoResults.Writer.WriteAsync(result);
            }));

            // hook up writer completion before starting to read to ensure the reader knows when it's done
            var searchCompletion = Task.WhenAll(searches).ContinueWith(t => videoResults.Writer.Complete());

            // start reading from result channel and return results as they are available
            await foreach (var result in videoResults.Reader.ReadAllAsync())
            {
                yield return result;
                cancellation.ThrowIfCancellationRequested();
            }

            await searchCompletion; // just to rethrow possible exceptions; should have completed at this point

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

        private async Task<Playlist> GetPlaylistAsync(SearchPlaylistCommand command, CancellationToken cancellation)
        {
            var storageKey = command.StorageKey;
            var playlist = await dataStore.GetAsync<Playlist>(storageKey); //get cached

            if (playlist == null //playlist cache is missing, outdated or lacking sufficient videos
                || playlist.Loaded < DateTime.UtcNow.AddHours(-Math.Abs(command.CacheHours))
                || playlist.Videos.Count < command.Top)
            {
                if (playlist == null) playlist = new Playlist();

                try
                {
                    // load and update videos in playlist while keeping existing video info
                    var freshVideos = await command.GetVideosAsync(youtube, cancellation).CollectAsync(command.Top);
                    playlist.Loaded = DateTime.UtcNow;

                    // use new order but append older entries; note that this leaves remotely deleted videos in the playlist
                    var freshKeys = freshVideos.Select(v => v.Id.Value).ToArray();

                    playlist.Videos = freshKeys.Concat(playlist.Videos.Keys.Except(freshKeys))
                        .ToDictionary(id => id, id => playlist.Videos.TryGetValue(id, out var uploaded) ? uploaded : null);

                    await dataStore.SetAsync(storageKey, playlist);
                }
                catch (PlaylistUnavailableException ex)
                {
                    // treat playlist identified by user input not being available as input error
                    if (command is SearchPlaylist searchPlaylist) throw new InputException(
                        $"Could not find {searchPlaylist.Label}'{searchPlaylist.Playlist}'.", ex);
                    else throw; // rethrow otherwise; the uploads playlist of a channel being unavailable is unexpected
                }
            }

            return playlist;
        }

        private async IAsyncEnumerable<VideoSearchResult> SearchUnindexedVideos(SearchPlaylistCommand command,
            string[] unIndexedVideoIds, VideoIndex index, [EnumeratorCancellation] CancellationToken cancellation,
            Func<IEnumerable<Video>, Task> updatePlaylistVideosUploaded)
        {
            cancellation.ThrowIfCancellationRequested();
            var storageKey = command.StorageKey;

            /* limit channel capacity to avoid holding a lot of loaded but unprocessed videos in memory
                SingleReader because we're reading from it synchronously */
            var unIndexedVideos = Channel.CreateBounded<Video>(new BoundedChannelOptions(5) { SingleReader = true });

            // load videos asynchronously in the background and put them on the unIndexedVideos channel for processing
            var loadVideos = Task.Run(async () =>
            {
                var downloads = new List<Task>();
                var loadLimiter = new SemaphoreSlim(5, 5);

                foreach (var id in unIndexedVideoIds)
                {
                    downloads.Add(Task.Run(async () =>
                    {
                        /*  pause task here before starting download until channel accepts another video
                            to avoid holding a lot of loaded but unprocessed videos in memory */
                        await loadLimiter.WaitAsync();

                        try
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var video = await GetVideoAsync(id, cancellation);
                            await unIndexedVideos.Writer.WriteAsync(video);
                        }
                        /* only start another download if channel has accepted the video or an error occurred */
                        finally { loadLimiter.Release(); }
                    }));
                }

                // complete writing after all download tasks finished
                await Task.WhenAll(downloads);
                unIndexedVideos.Writer.Complete();
            });

            var uncommitted = new List<Video>(); // batch of loaded and indexed, but uncommited video index changes

            // local getter preferring to reuse already loaded video from uncommitted bag for better performance
            Func<string, CancellationToken, Task<Video>> getVideoAsync = async (videoId, cancellation)
                => uncommitted.SingleOrDefault(v => v.Id == videoId) ?? await GetVideoAsync(videoId, cancellation);

            // read synchronously from the channel because we're writing to the same video index
            await foreach (var video in unIndexedVideos.Reader.ReadAllAsync())
            {
                cancellation.ThrowIfCancellationRequested();

                if (uncommitted.Count == 0)
                {
                    index.BeginBatchChange();
                }

                await index.AddAsync(video, cancellation);
                uncommitted.Add(video);

                // save batch of changes
                if (uncommitted.Count >= 5 // to prevent the batch from growing too big
                    || unIndexedVideos.Reader.Completion.IsCompleted // to save remaining changes
                    || unIndexedVideos.Reader.Count == 0) // to use resources efficiently while we've got nothing queued up for indexing
                {
                    await Task.WhenAll(index.CommitBatchChangeAsync(), updatePlaylistVideosUploaded(uncommitted));

                    var indexedVideoInfos = uncommitted.ToDictionary(v => v.Id, v => v.Uploaded as DateTime?);

                    // search after committing index changes to output matches as we go
                    await foreach (var result in index.SearchAsync(command, getVideoAsync, cancellation, indexedVideoInfos))
                        yield return result;

                    uncommitted.Clear(); // safe to do because we're reading synchronously and no other thread could have added to it in between
                }
            }

            await loadVideos; // just to rethrow possible exceptions; should have completed at this point
        }

        /// <summary>Searches videos according to the specified command.</summary>
        /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
        /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
        internal async IAsyncEnumerable<VideoSearchResult> SearchVideosAsync(
            SearchVideos command, [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            foreach (var videoId in command.ValidIds)
            {
                cancellation.ThrowIfCancellationRequested();
                var storageKey = Video.StorageKeyPrefix + videoId;
                var index = await videoIndexRepo.GetAsync(storageKey);

                // used to get a video during search
                Func<string, CancellationToken, Task<Video>> getVideoAsync = (videoId, cancellation)
                    => GetVideoAsync(videoId, cancellation);

                if (index == null)
                {
                    index = videoIndexRepo.Build(storageKey);
                    var video = await GetVideoAsync(videoId, cancellation);
                    index.BeginBatchChange();
                    await index.AddAsync(video, cancellation);
                    await index.CommitBatchChangeAsync();

                    // reuse already loaded video for better performance
                    getVideoAsync = (videoId, cancellation) => Task.FromResult(video);
                }

                await foreach (var result in index.SearchAsync(command, getVideoAsync, cancellation))
                    yield return result;
            }
        }

        private async Task<Video> GetVideoAsync(string videoId, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var storageKey = Video.StorageKeyPrefix + videoId;
            var video = await dataStore.GetAsync<Video>(storageKey);

            if (video == null)
            {
                try
                {
                    var vid = await youtube.Videos.GetAsync(videoId, cancellation);
                    video = MapVideo(vid);
                    video.UnIndexed = true; // to re-index it if it was already indexed

                    await foreach (var track in DownloadCaptionTracksAsync(videoId, cancellation))
                        video.CaptionTracks.Add(track);

                    await dataStore.SetAsync(storageKey, video);
                }
                catch (HttpRequestException ex)
                {
                    if (ex.IsNotFound()) throw new InputException($"Video '{videoId}' could not be found.", ex);
                    else throw;
                }
            }

            /* Sanitize captions, making sure cached captions as well as downloaded
                are cleaned of duplicates and ordered by time.
                This may be moved into DownloadCaptionTracksAsync() in a future version
                when we can be reasonably sure caches in the wild are sanitized. */
            foreach (var track in video.CaptionTracks)
                track.Captions = track.Captions.Distinct().OrderBy(c => c.At).ToList();

            return video;
        }

        private static Video MapVideo(YoutubeExplode.Videos.Video video) => new Video
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
            var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoId);

            foreach (var trackInfo in trackManifest.Tracks)
            {
                cancellation.ThrowIfCancellationRequested();
                var captionTrack = new CaptionTrack { LanguageName = trackInfo.Language.Name, Url = trackInfo.Url };

                try
                {
                    // Get the actual closed caption track
                    var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo, cancellation);

                    captionTrack.Captions = track.Captions
                        .Select(c => new Caption { At = Convert.ToInt32(c.Offset.TotalSeconds), Text = c.Text })
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
    }

    internal sealed class VideoSearchResult
    {
        internal Video Video { get; set; }
        internal PaddedMatch TitleMatches { get; set; }
        internal PaddedMatch[] DescriptionMatches { get; set; }
        internal PaddedMatch[] KeywordMatches { get; set; }
        internal CaptionTrackResult[] MatchingCaptionTracks { get; set; }

        internal sealed class CaptionTrackResult
        {
            public CaptionTrack Track { get; set; }
            internal List<(PaddedMatch, Caption)> Matches { get; set; }
        }
    }
}