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

        internal async IAsyncEnumerable<Tuple<Video, Caption[]>> SearchPlaylistAsync(SearchPlaylist command)
        {
            var playlist = await dataStore.GetAsync<Playlist>(command.PlaylistId); //get cached

            if (playlist == null || playlist.Videos.Count < command.Latest
                || playlist.Loaded < DateTime.UtcNow.AddMinutes(-command.CachePlaylistForMinutes))
            {
                playlist = new Playlist { Loaded = DateTime.UtcNow };
                var current = 0;

                await foreach (var vid in youtube.Playlists.GetVideosAsync(command.PlaylistId))
                {
                    var video = new Video
                    {
                        Id = vid.Id.Value,
                        Uploaded = vid.UploadDate.UtcDateTime
                    };

                    playlist.Videos.Add(video);

                    await foreach (var captions in SearchVideoAsync(video.Id, command.Terms))
                    {
                        if (captions.Any()) yield return Tuple.Create(video, captions);
                    }

                    if (current < command.Latest) current++;
                    else break;
                }

                await dataStore.SetAsync(command.PlaylistId, playlist)
                    .ConfigureAwait(false); //nothing else to do here
            }
            else
            {
                foreach (var video in playlist.Videos.Take(command.Latest))
                {
                    await foreach (var captions in SearchVideoAsync(video.Id, command.Terms))
                    {
                        if (captions.Any()) yield return Tuple.Create(video, captions);
                    }
                }
            }
        }

        internal async IAsyncEnumerable<Tuple<string, Caption[]>> SearchVideosAsync(SearchVideos command)
        {
            foreach (var videoId in command.VideoIds)
            {
                await foreach (var captions in SearchVideoAsync(videoId, command.Terms))
                {
                    if (captions.Any()) yield return Tuple.Create(videoId, captions);
                }
            }
        }

        private async IAsyncEnumerable<Caption[]> SearchVideoAsync(string videoId, IEnumerable<string> terms)
        {
            var tracks = await dataStore.GetAsync<List<CaptionTrack>>(videoId);

            if (tracks == null)
            {
                tracks = new List<CaptionTrack>();

                await foreach (var track in DownloadCaptionTracksAsync(videoId))
                {
                    tracks.Add(track);
                    yield return track.FindCaptions(terms);
                }

                await dataStore.SetAsync(videoId, tracks)
                    .ConfigureAwait(false); //nothing else to do here
            }
            else
            {
                foreach (var track in tracks)
                {
                    yield return track.FindCaptions(terms);
                }
            }
        }

        private async IAsyncEnumerable<CaptionTrack> DownloadCaptionTracksAsync(string videoId)
        {
            var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoId);

            foreach (var trackInfo in trackManifest.Tracks)
            {
                // Get the actual closed caption track
                var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo);

                yield return new CaptionTrack
                {
                    Captions = track.Captions
                        .Select(c => new Caption { At = Convert.ToInt32(c.Offset.TotalSeconds), Text = c.Text })
                        .ToArray()
                };
            }
        }
    }
}