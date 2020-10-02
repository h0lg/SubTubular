using System;
using System.Linq;
using System.Collections.Generic;
using YoutubeExplode;
using System.Threading.Tasks;

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

        internal async IAsyncEnumerable<VideoSearchResult> SearchPlaylistAsync(SearchPlaylist command)
        {
            var playlist = await dataStore.GetAsync<Playlist>(command.PlaylistId); //get cached

            if (playlist == null || playlist.VideoIds.Count < command.Latest
                || playlist.Loaded < DateTime.UtcNow.AddHours(-command.CachePlaylistForHours))
            {
                playlist = new Playlist { Loaded = DateTime.UtcNow };

                /* get the entire list. underlying implementation only makes one HTTP request either way
                    and we don't know how the videos are ordererd:
                    special "Uploads" playlist is latest uploaded first, but custom playlists may not be */
                var videos = await youtube.Playlists.GetVideosAsync(command.PlaylistId);

                //so order videos explicitly by latest uploaded before taking the latest n videos
                foreach (var vid in videos.OrderByDescending(v => v.UploadDate).Take(command.Latest))
                {
                    playlist.VideoIds.Add(vid.Id);

                    var video = await GetVideoAsync(vid.Id, vid);
                    var searchResult = SearchVideo(video, command.Terms);
                    if (searchResult != null) yield return searchResult;
                }

                await dataStore.SetAsync(command.PlaylistId, playlist)
                    .ConfigureAwait(false); //nothing else to do here
            }
            else
            {
                //playlist.VideoIds is already ordered latest uploaded first; see above
                foreach (var videoId in playlist.VideoIds.Take(command.Latest))
                {
                    var video = await GetVideoAsync(videoId);
                    var searchResult = SearchVideo(video, command.Terms);
                    if (searchResult != null) yield return searchResult;
                }
            }
        }

        internal async IAsyncEnumerable<VideoSearchResult> SearchVideosAsync(SearchVideos command)
        {
            foreach (var videoId in command.VideoIds)
            {
                var video = await GetVideoAsync(videoId);
                var searchResult = SearchVideo(video, command.Terms);
                if (searchResult != null) yield return searchResult;
            }
        }

        private VideoSearchResult SearchVideo(Video video, IEnumerable<string> terms)
        {
            var titleMatches = video.Title.ContainsAny(terms);
            var matchingKeywords = video.Keywords.Where(kw => kw.ContainsAny(terms)).ToArray();

            var matchingCaptionTracks = video.CaptionTracks
                .Select(track =>
                {
                    var matchingCaptions = track.Captions.Where(c => c.Text.ContainsAny(terms)).ToArray();
                    return matchingCaptions.Any() ? new CaptionTrack(track, matchingCaptions) : null;
                })
                .Where(match => match != null)
                .ToArray();

            var matchingDescriptionLines = video.Description
                //split on newlines, see https://stackoverflow.com/a/1547483
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l) && l.ContainsAny(terms))
                .ToArray();

            return titleMatches || matchingDescriptionLines.Any() || matchingKeywords.Any() || matchingCaptionTracks.Any()
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

        private async Task<Video> GetVideoAsync(string videoId, YoutubeExplode.Videos.Video alreadyLoaded = null)
        {
            var video = await dataStore.GetAsync<Video>(videoId);

            if (video == null)
            {
                var vid = alreadyLoaded ?? await youtube.Videos.GetAsync(videoId);
                video = MapVideo(vid);

                await foreach (var track in DownloadCaptionTracksAsync(videoId))
                {
                    video.CaptionTracks.Add(track);
                }

                await dataStore.SetAsync(videoId, video);
            }

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

        private async IAsyncEnumerable<CaptionTrack> DownloadCaptionTracksAsync(string videoId)
        {
            var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoId);

            foreach (var trackInfo in trackManifest.Tracks)
            {
                // Get the actual closed caption track
                var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo);

                yield return new CaptionTrack
                {
                    LanguageName = trackInfo.Language.Name,
                    Captions = track.Captions
                        .Select(c => new Caption { At = Convert.ToInt32(c.Offset.TotalSeconds), Text = c.Text })
                        .ToArray()
                };
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