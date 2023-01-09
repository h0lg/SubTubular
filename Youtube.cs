using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;

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

        /// <summary>Searches videos defined by a playlist.</summary>
        /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
        /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
        internal async IAsyncEnumerable<VideoSearchResult> SearchPlaylistAsync(
            SearchPlaylistCommand command, [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var storageKey = command.StorageKey;
            var playlist = await dataStore.GetAsync<Playlist>(storageKey); //get cached

            var index = await videoIndexRepo.GetAsync(storageKey);
            if (index == null) index = videoIndexRepo.Build();
            string[] videoIds;

            if (playlist == null //playlist cache is missing, outdated or lacking sufficient videos
                || playlist.Loaded < DateTime.UtcNow.AddHours(-Math.Abs(command.CacheHours))
                || playlist.VideoIds.Count < command.Top)
            {
                // load and update videos in playlist
                playlist = new Playlist { Loaded = DateTime.UtcNow };
                var playlistVideos = await command.GetVideosAsync(youtube, cancellation).CollectAsync(command.Top);
                playlist.VideoIds = videoIds = playlistVideos.Select(v => v.Id.Value).ToArray();
                await dataStore.SetAsync(storageKey, playlist);
            }
            else videoIds = playlist.VideoIds.Take(command.Top).ToArray(); // read from cache

            var indexedVideoIds = index.GetIndexed(videoIds);

            // search already indexed videos in one go
            await foreach (var result in index.SearchAsync(command, GetVideoAsync, cancellation, indexedVideoIds))
                yield return result;

            var unIndexedVideoIds = videoIds.Except(indexedVideoIds).ToArray();

            // load, index, search and output results for un-indexed videos
            if (unIndexedVideoIds.Length > 0)
            {
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

                var unsaved = new List<Video>(); // batch of loaded and indexed, but uncommited video index changes

                // local getter preferring to reuse already loaded video from unsaved bag for better performance
                Func<string, CancellationToken, ValueTask<Video>> getVideoAsync = async (videoId, cancellation)
                    => unsaved.SingleOrDefault(v => v.Id == videoId) ?? await GetVideoAsync(videoId, cancellation);

                // read synchronously from the channel because we're writing to the same video index
                await foreach (var video in unIndexedVideos.Reader.ReadAllAsync())
                {
                    cancellation.ThrowIfCancellationRequested();

                    if (unsaved.Count == 0)
                    {
                        index.BeginBatchChange();
                    }

                    await index.AddAsync(video, cancellation);
                    unsaved.Add(video);

                    // save batch of changes
                    if (unsaved.Count >= 5 // if the batch grows too big
                        || unIndexedVideos.Reader.Completion.IsCompleted // to save remaining changes
                        || unIndexedVideos.Reader.Count == 0) // if we have the time because there's no work waiting
                    {
                        await index.CommitBatchChangeAsync();
                        await videoIndexRepo.SaveAsync(index, storageKey);

                        // search after committing index changes to output matches as we go
                        await foreach (var result in index.SearchAsync(command, getVideoAsync, cancellation, unsaved.Select(v => v.Id).ToArray()))
                            yield return result;

                        unsaved.Clear(); // safe to do because we're reading synchronously and no other thread could have added to it in between
                    }
                }

                await loadVideos; // just to rethrow possible exceptions; should have completed at this point
            }
        }

        /// <summary>Searches videos according to the specified command.</summary>
        /// <param name="cancellation">Passed in either explicitly or by the IAsyncEnumerable.WithCancellation() extension,
        /// see https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8#a-tour-through-async-enumerables</param>
        internal async IAsyncEnumerable<VideoSearchResult> SearchVideosAsync(
            SearchVideos command, [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            foreach (var videoId in command.GetVideoIds())
            {
                cancellation.ThrowIfCancellationRequested();
                var index = await videoIndexRepo.GetAsync(videoId);

                if (index == null)
                {
                    index = videoIndexRepo.Build();
                    var video = await GetVideoAsync(videoId, cancellation);
                    index.BeginBatchChange();
                    await index.AddAsync(video, cancellation);
                    await index.CommitBatchChangeAsync();
                    await videoIndexRepo.SaveAsync(index, videoId);
                }

                await foreach (var result in index.SearchAsync(command, GetVideoAsync, cancellation))
                    yield return result;
            }
        }

        private async ValueTask<Video> GetVideoAsync(string videoId, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var video = await dataStore.GetAsync<Video>(videoId);

            if (video == null)
            {
                var vid = await youtube.Videos.GetAsync(videoId, cancellation);
                video = MapVideo(vid);

                await foreach (var track in DownloadCaptionTracksAsync(videoId, cancellation))
                    video.CaptionTracks.Add(track);

                await dataStore.SetAsync(videoId, video);
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