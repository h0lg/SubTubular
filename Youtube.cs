using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lifti;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace SubTubular
{
    internal sealed class Youtube
    {
        private readonly YoutubeClient youtube = new YoutubeClient();
        private readonly DataStore dataStore;
        private readonly VideoIndex videoIndex;

        // ensures only one process writes to a video index at a time because they're shared across a playlist
        private readonly SemaphoreSlim indexWriteLock = new SemaphoreSlim(1, 1);

        internal Youtube(DataStore dataStore, VideoIndex videoIndex)
        {
            this.dataStore = dataStore;
            this.videoIndex = videoIndex;
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

            var index = await videoIndex.GetAsync(storageKey);
            if (index == null) index = videoIndex.Create();
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

            #region load, index, search and output results for un-indexed videos
            var unIndexedVideoIds = videoIds.Where(id => !index.Items.Contains(id)).ToArray();
            var unIndexedSearches = unIndexedVideoIds.Select(videoId => SearchUnIndexedVideo(command, videoId, index, cancellation));

            foreach (var completion in unIndexedSearches.Interleaved()) // yield results of parallel searches as they complete
            {
                var search = await completion;
                var result = await search;
                if (result != null) yield return result;
            }
            #endregion

            // search remaining already indexed videos in one go
            var indexedVideoIds = videoIds.Except(unIndexedVideoIds).ToArray();

            await foreach (var match in Search(index, command, cancellation))
            {
                if (indexedVideoIds.Contains(match.Video.Id)) yield return match;
            }

            if (unIndexedVideoIds.Any()) await videoIndex.SaveAsync(index, storageKey);
        }

        private async Task<VideoSearchResult> SearchUnIndexedVideo(SearchCommand command,
            string videoId, FullTextIndex<string> index, CancellationToken cancellation)
        {
            await indexWriteLock.WaitAsync(); // limit write access to index to avoid timeout waiting for write lock
            await IndexVideo(videoId, index, cancellation);
            indexWriteLock.Release();
            VideoSearchResult result = null;

            // search after each loaded and indexed video to output matches as we go
            await foreach (var match in Search(index, command, cancellation))
            {
                // but only output the match for this video, if any
                if (videoId == match.Video.Id)
                {
                    result = match;
                    break;
                }
            }

            return result;
        }

        private async Task IndexVideo(string videoId, FullTextIndex<string> index, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var video = await GetVideoAsync(videoId, cancellation);
            cancellation.ThrowIfCancellationRequested();
            index.BeginBatchChange(); // see https://mikegoatly.github.io/lifti/docs/indexing-mutations/batch-mutations/
            await index.AddAsync(video);

            foreach (var track in video.CaptionTracks)
            {
                cancellation.ThrowIfCancellationRequested();
                await index.AddAsync(videoId + "#" + track.LanguageName, track.FullText);
            }

            await index.CommitBatchChangeAsync();
        }

        private async IAsyncEnumerable<VideoSearchResult> Search(FullTextIndex<string> index, SearchCommand command,
            [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            var results = index.Search(command.Query);

            var resultsByVideoId = results.Select(result =>
            {
                var ids = result.Key.Split('#');
                var videoId = ids[0];
                var language = ids.Length > 1 ? ids[1] : null;
                return new { videoId, language, result };
            }).GroupBy(m => m.videoId);

            foreach (var group in resultsByVideoId)
            {
                var video = await GetVideoAsync(group.Key, cancellation);
                var result = new VideoSearchResult { Video = video };
                var metaDataMatch = group.SingleOrDefault(m => m.language == null);

                if (metaDataMatch != null)
                {
                    var titleMatches = metaDataMatch.result.FieldMatches.Where(m => m.FoundIn == nameof(Video.Title));
                    if (titleMatches.Any()) result.TitleMatches = new PaddedMatch(video.Title,
                        titleMatches.SelectMany(m => m.Locations)
                            .Select(m => new PaddedMatch.IncludedMatch { Start = m.Start, Length = m.Length }).ToArray());

                    result.DescriptionMatches = metaDataMatch.result.FieldMatches
                        .Where(m => m.FoundIn == nameof(Video.Description))
                        .SelectMany(m => m.Locations)
                        .Select(l => new PaddedMatch(l.Start, l.Length, command.Padding, video.Description))
                        .MergeOverlapping(video.Description)
                        .ToArray();

                    var keywordMatches = metaDataMatch.result.FieldMatches
                        .Where(m => m.FoundIn == nameof(Video.Keywords))
                        .ToArray();

                    if (keywordMatches.Any())
                    {
                        var joinedKeywords = string.Empty;

                        // remembers the index in the list of keywords and start index in joinedKeywords for each keyword
                        var keywordInfos = video.Keywords.Select((keyword, index) =>
                        {
                            var info = new { index, Start = joinedKeywords.Length };
                            joinedKeywords += keyword;
                            return info;
                        }).ToArray();

                        result.KeywordMatches = keywordMatches.SelectMany(match => match.Locations)
                            .Select(location => new
                            {
                                location, // represents the match location in joinedKeywords
                                // used to calculate the match index within a matched keyword
                                keywordInfo = keywordInfos.TakeWhile(info => info.Start <= location.Start).Last()
                            })
                            .GroupBy(x => x.keywordInfo.index) // group matches by keyword
                            .Select(g => new PaddedMatch(video.Keywords[g.Key],
                                g.Select(x => new PaddedMatch.IncludedMatch
                                {
                                    // recalculate match index relative to keyword start
                                    Start = x.location.Start - x.keywordInfo.Start,
                                    Length = x.location.Length
                                }).ToArray()))
                            .ToArray();
                    }
                }

                result.MatchingCaptionTracks = group.Where(m => m.language != null).Select(m =>
                {
                    var track = video.CaptionTracks.SingleOrDefault(t => t.LanguageName == m.language);

                    var matches = m.result.FieldMatches.First().Locations
                        // use a temporary/transitory PaddedMatch to ensure the minumum configured padding
                        .Select(l => new PaddedMatch(l.Start, l.Length, command.Padding, track.FullText))
                        .MergeOverlapping(track.FullText)
                        /*  map transitory padded match to captions containing it and a new padded match
                            with adjusted included matches containing the joined text of the matched caption */
                        .Select(match =>
                        {
                            // find first and last captions containing parts of the padded match
                            var first = track.CaptionAtFullTextIndex.Last(x => x.Value <= match.Start);
                            var last = track.CaptionAtFullTextIndex.Last(x => first.Value <= x.Value && x.Value <= match.End);

                            var captions = track.CaptionAtFullTextIndex // span of captions containing the padded match
                                .Where(x => first.Value <= x.Value && x.Value <= last.Value).ToArray();

                            // return a single caption for all captions containing the padded match
                            var joinedCaption = new Caption
                            {
                                At = first.Key.At,
                                Text = captions.Select(x => x.Key.Text)
                                    .Where(text => !string.IsNullOrWhiteSpace(text)) // skip included line breaks
                                    .Select(text => text.NormalizeWhiteSpace(CaptionTrack.FullTextSeperator)) // replace included line breaks
                                    .Join(CaptionTrack.FullTextSeperator)
                            };

                            return Tuple.Create(new PaddedMatch(match, joinedCaption, first.Value), joinedCaption);
                        })
                        .OrderBy(tuple => tuple.Item2.At).ToList(); // return captions in order

                    return new VideoSearchResult.CaptionTrackResult { Track = track, Matches = matches };
                }).ToArray();

                yield return result;
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
                var index = await videoIndex.GetAsync(videoId);

                if (index == null)
                {
                    index = videoIndex.Create();
                    await IndexVideo(videoId, index, cancellation);
                    await videoIndex.SaveAsync(index, videoId);
                }

                await foreach (var result in Search(index, command, cancellation))
                    yield return result;
            }
        }

        private ConcurrentDictionary<string, Video> videoById = new ConcurrentDictionary<string, Video>();

        private async ValueTask<Video> GetVideoAsync(string videoId, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (videoById.ContainsKey(videoId)) return videoById[videoId];
            var video = await dataStore.GetAsync<Video>(videoId);

            if (video == null)
            {
                var vid = await youtube.Videos.GetAsync(videoId, cancellation);
                video = MapVideo(vid);

                await foreach (var track in DownloadCaptionTracksAsync(videoId, cancellation))
                    video.CaptionTracks.Add(track);

                await dataStore.SetAsync(videoId, video);
            }

            videoById[videoId] = video;

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
            internal List<Tuple<PaddedMatch, Caption>> Matches { get; set; }
        }
    }
}