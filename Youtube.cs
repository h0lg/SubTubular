using System;
using System.Linq;
using System.Collections.Generic;
using YoutubeExplode;

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

        internal IDictionary<Video, Caption[]> SearchPlaylist(SearchPlaylist command)
        {
            var playlist = dataStore.Get<Playlist>(command.PlaylistId); //get cached

            if (playlist == null || playlist.Videos.Length < command.Latest
                || playlist.Loaded < DateTime.UtcNow.AddMinutes(-command.CachePlaylistForMinutes))
            {
                var loaded = DateTime.UtcNow;
                var videos = youtube.Playlists.GetVideosAsync(command.PlaylistId).BufferAsync(command.Latest).Result;

                playlist = new Playlist
                {
                    Loaded = loaded,
                    Videos = videos
                        .Select(v => new Video
                        {
                            Id = v.Id.Value,
                            Uploaded = v.UploadDate.UtcDateTime
                        })
                        .ToArray()
                };

                dataStore.Set(command.PlaylistId, playlist);
            }

            var captionsByVideo = new Dictionary<Video, Caption[]>();

            foreach (var video in playlist.Videos.Take(command.Latest))
            {
                var filtered = SearchVideo(video.Id, command.Terms);
                if (filtered.Any()) captionsByVideo.Add(video, filtered);
            }

            return captionsByVideo;
        }

        internal IDictionary<string, Caption[]> SearchVideos(SearchVideos command)
        {
            var captionsByVideoId = new Dictionary<string, Caption[]>();

            foreach (var videoId in command.VideoIds)
            {
                var filtered = SearchVideo(videoId, command.Terms);
                if (filtered.Any()) captionsByVideoId.Add(videoId, filtered);
            }

            return captionsByVideoId;
        }

        private Caption[] SearchVideo(string videoId, IEnumerable<string> terms)
        {
            var tracks = dataStore.Get<CaptionTrack[]>(videoId);

            if (tracks == null)
            {
                tracks = DownloadCaptionTracks(videoId);
                dataStore.Set(videoId, tracks);
            }

            return tracks
                .SelectMany(t => t.Captions)
                .Where(c => terms.Any(t => c.Text.Contains(t, StringComparison.InvariantCultureIgnoreCase)))
                .ToArray();
        }

        private CaptionTrack[] DownloadCaptionTracks(string videoId)
        {
            var captions = new List<CaptionTrack>();
            var trackManifest = youtube.Videos.ClosedCaptions.GetManifestAsync(videoId).Result;

            foreach (var trackInfo in trackManifest.Tracks)
            {
                // Get the actual closed caption track
                var track = youtube.Videos.ClosedCaptions.GetAsync(trackInfo).Result;

                captions.Add(new CaptionTrack
                {
                    Captions = track.Captions
                        .Select(c => new Caption { At = Convert.ToInt32(c.Offset.TotalSeconds), Text = c.Text })
                        .ToArray()
                });
            }

            return captions.ToArray();
        }
    }
}