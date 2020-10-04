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

                /* get the entire list. underlying implementation only makes one HTTP request either way
                    and we don't know how the videos are ordererd:
                    special "Uploads" playlist is latest uploaded first, but custom playlists may not be */
                var videos = await youtube.Playlists.GetVideosAsync(command.PlaylistId);

                //so order videos explicitly by latest uploaded before taking the latest n videos
                foreach (var vid in videos.OrderByDescending(v => v.UploadDate).Take(command.Latest))
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
                }

                await dataStore.SetAsync(command.PlaylistId, playlist)
                    .ConfigureAwait(false); //nothing else to do here
            }
            else
            {
                //playlist.Videos is already ordered latest uploaded first; see above
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