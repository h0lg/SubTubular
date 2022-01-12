using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace SubTubular
{
    internal sealed class Youtube
    {
        private readonly DataStore dataStore;
        private readonly YoutubeClient youtube;

        internal Youtube(DataStore dataStore)
        {
            this.dataStore = dataStore;
            youtube = new YoutubeClient();
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

            if (playlist == null //playlist cache is missing, outdated or lacking sufficient videos
                || playlist.Loaded < DateTime.UtcNow.AddHours(-command.CacheHours)
                || playlist.VideoIds.Count < command.Top)
            {
                playlist = new Playlist { Loaded = DateTime.UtcNow };
                var playlistVideos = await command.GetVideosAsync(youtube, cancellation).CollectAsync(command.Top);
                playlist.VideoIds = playlistVideos.Select(v => v.Id.Value).ToList();
                await dataStore.SetAsync(storageKey, playlist);

                foreach (var playlistVideo in playlistVideos)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var video = await GetVideoAsync(playlistVideo.Id, cancellation);

                    var searchResult = SearchVideo(video, command.Terms);
                    if (searchResult != null) yield return searchResult;
                }
            }
            else // read from cache
            {
                foreach (var videoId in playlist.VideoIds.Take(command.Top))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var video = await GetVideoAsync(videoId, cancellation);
                    var searchResult = SearchVideo(video, command.Terms);
                    if (searchResult != null) yield return searchResult;
                }
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
                var video = await GetVideoAsync(videoId, cancellation);
                var searchResult = SearchVideo(video, command.Terms);
                if (searchResult != null) yield return searchResult;
            }
        }

        private VideoSearchResult SearchVideo(Video video, IEnumerable<string> terms)
        {
            var titleMatches = video.Title.ContainsAny(terms);
            var matchingKeywords = video.Keywords.Where(kw => kw.ContainsAny(terms)).ToArray();
            var matchingCaptionTracks = SearchCaptionTracks(video.CaptionTracks, terms);

            var matchingDescriptionLines = video.Description.SplitOnNewLines()
                .Where(line => !string.IsNullOrWhiteSpace(line) && line.ContainsAny(terms))
                .ToArray();

            return titleMatches || matchingDescriptionLines.Any() || matchingKeywords.Any()
                || matchingCaptionTracks.Any() || video.CaptionTracks.Any(t => t.Error != null)
                ? new VideoSearchResult
                {
                    Video = video,
                    TitleMatches = titleMatches,
                    MatchingDescriptionLines = matchingDescriptionLines,
                    MatchingKeywords = matchingKeywords,
                    MatchingCaptionTracks = matchingCaptionTracks
                }
                : null;
        }

        private static CaptionTrack[] SearchCaptionTracks(IList<CaptionTrack> tracks, IEnumerable<string> terms) => tracks
            .Where(track => track.Error == null)
            .Select(track =>
            {
                var words = terms.Where(term => !term.Contains(' ')).ToArray();
                var matching = track.Captions.Where(caption => caption.Text.ContainsAny(words)).ToList();
                var phrases = terms.Except(words).ToArray();

                if (phrases.Any()) matching.AddRange(SearchCaptionTrack(track, phrases));
                if (matching.Count == 0) return null;

                return new CaptionTrack(track, matching
                    //only take longest caption at location
                    .GroupBy(c => c.At).Select(g => g.OrderBy(c => c.Text.Length).Last())
                    .OrderBy(c => c.At) //return captions in order
                    .ToList());
            })
            .Where(matchingTrack => matchingTrack != null)
            .ToArray();

        private static IEnumerable<Caption> SearchCaptionTrack(CaptionTrack track, string[] phrases)
        {
            const string fullTextSeperator = " ";
            var captionAtIndex = new Dictionary<Caption, int>();

            //aggregate captions into fullText to enable matching phrases across caption boundaries
            var fullText = track.Captions.Aggregate(string.Empty, (fullText, caption) =>
            {
                //remember at what index in the fullText the caption starts
                captionAtIndex.Add(caption, fullText.Length == 0 ? 0 : fullText.Length + fullTextSeperator.Length);

                return fullText.Length == 0 ? caption.Text : fullText + fullTextSeperator + caption.Text;
            });

            return fullText.GetMatches(phrases).Select(match =>
            {
                //find first and last captions containing parts of the phrase
                var first = captionAtIndex.Last(x => x.Value <= match.Index);
                var last = captionAtIndex.Last(x => first.Value <= x.Value && x.Value < match.Index + match.Length);

                //return a single caption for all captions containing the phrase
                return new Caption
                {
                    At = first.Key.At,
                    Text = captionAtIndex
                        .Where(x => first.Value <= x.Value && x.Value <= last.Value)
                        .Select(x => x.Key.Text)
                        .Join(fullTextSeperator)
                };
            });
        }

        private async Task<Video> GetVideoAsync(string videoId, CancellationToken cancellation)
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

                YoutubeExplode.Videos.ClosedCaptions.ClosedCaptionTrack track;

                try
                {
                    // Get the actual closed caption track
                    track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo, cancellation);

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
        public Video Video { get; set; }
        public bool TitleMatches { get; set; }
        public string[] MatchingDescriptionLines { get; set; }
        public string[] MatchingKeywords { get; set; }
        public CaptionTrack[] MatchingCaptionTracks { get; set; }
    }
}