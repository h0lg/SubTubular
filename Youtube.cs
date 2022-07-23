using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lifti;
using Nito.AsyncEx;
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

            #region load, index, search and output results for un-indexed videos
            var unIndexedVideoIds = await index.GetUnIndexedVideoIdsAsync(videoIds);
            var unIndexedSearches = unIndexedVideoIds.Select(videoId => SearchUnIndexedVideoAsync(command, videoId, index, cancellation));

            /* yields results of parallel searches as they complete;
                see https://github.com/StephenCleary/AsyncEx/blob/master/doc/TaskExtensions.md */
            foreach (var search in unIndexedSearches.OrderByCompletion())
            {
                var result = search.Result;
                if (result != null) yield return result;
            }
            #endregion

            // search remaining already indexed videos in one go
            var indexedVideoIds = videoIds.Except(unIndexedVideoIds).ToArray();

            await foreach (var match in SearchAsync(index, command, cancellation))
            {
                if (indexedVideoIds.Contains(match.Video.Id)) yield return match;
            }

            if (unIndexedVideoIds.Any()) await videoIndexRepo.SaveAsync(index, storageKey);
        }

        private async Task<VideoSearchResult> SearchUnIndexedVideoAsync(SearchPlaylistCommand command,
            string videoId, VideoIndex index, CancellationToken cancellation)
        {
            await IndexVideoAsync(videoId, index, command.StorageKey, cancellation);
            VideoSearchResult result = null;

            // search after each loaded and indexed video to output matches as we go
            await foreach (var match in SearchAsync(index, command, cancellation))
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

        private async Task IndexVideoAsync(string videoId, VideoIndex index, string storageKey, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var video = await GetVideoAsync(videoId, cancellation);
            await index.AddAsync(video, cancellation, () => videoIndexRepo.SaveAsync(index, storageKey));
        }

        private async IAsyncEnumerable<VideoSearchResult> SearchAsync(VideoIndex index, SearchCommand command,
            [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            var results = await index.SearchAsync(command.Query, cancellation);

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
                    var fullText = track.GetFullText();
                    var captionAtFullTextIndex = track.GetCaptionAtFullTextIndex();

                    var matches = m.result.FieldMatches.First().Locations
                        // use a temporary/transitory PaddedMatch to ensure the minumum configured padding
                        .Select(l => new PaddedMatch(l.Start, l.Length, command.Padding, fullText))
                        .MergeOverlapping(fullText)
                        /*  map transitory padded match to captions containing it and a new padded match
                            with adjusted included matches containing the joined text of the matched caption */
                        .Select(match =>
                        {
                            // find first and last captions containing parts of the padded match
                            var first = captionAtFullTextIndex.Last(x => x.Key <= match.Start);
                            var last = captionAtFullTextIndex.Last(x => first.Key <= x.Key && x.Key <= match.End);

                            var captions = captionAtFullTextIndex // span of captions containing the padded match
                                .Where(x => first.Key <= x.Key && x.Key <= last.Key).ToArray();

                            // return a single caption for all captions containing the padded match
                            var joinedCaption = new Caption
                            {
                                At = first.Value.At,
                                Text = captions.Select(x => x.Value.Text)
                                    .Where(text => !string.IsNullOrWhiteSpace(text)) // skip included line breaks
                                    .Select(text => text.NormalizeWhiteSpace(CaptionTrack.FullTextSeperator)) // replace included line breaks
                                    .Join(CaptionTrack.FullTextSeperator)
                            };

                            return Tuple.Create(new PaddedMatch(match, joinedCaption, first.Key), joinedCaption);
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
                var index = await videoIndexRepo.GetAsync(videoId);

                if (index == null)
                {
                    index = videoIndexRepo.Build();
                    await IndexVideoAsync(videoId, index, videoId, cancellation);
                }

                await foreach (var result in SearchAsync(index, command, cancellation))
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