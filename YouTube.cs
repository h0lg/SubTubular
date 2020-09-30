using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using System.Linq;
using System.Collections.Generic;

namespace SubTubular
{
    internal sealed class YouTube
    {
        private static string appName = typeof(YouTube).Namespace;
        private static readonly FileDataStore dataStore = new FileDataStore(appName);

        #region API key
        private static string apiKeyKey = appName + "-api-key";
        private string token;

        internal static void SetApiKey(string apiKey) => dataStore.StoreAsync(apiKeyKey, apiKey);
        internal static string GetApiKey() => dataStore.GetAsync<string>(apiKeyKey).Result;
        #endregion

        private readonly YouTubeService youtubeService;

        // internal YouTube() => youtubeService = new YouTubeService(new BaseClientService.Initializer
        // {
        //     HttpClientInitializer = AuthorizeWithOAuthAsync().Result,
        //     ApplicationName = appName
        // });

        internal YouTube(string apiKey) => youtubeService = new YouTubeService(new BaseClientService.Initializer
        {
            // ApiKey = apiKey,
            HttpClientInitializer = AuthorizeWithOAuthAsync().Result,
            ApplicationName = appName
        });

        internal string[] SearchPlaylist(SearchPlaylist command)
        {
            //search cache for playlist
            var cache = dataStore.GetAsync<PlaylistCache>(command.PlaylistId).Result;

            if (cache == null || cache.Loaded < DateTime.UtcNow.AddMinutes(-command.CachePlaylistForMinutes))
            {
                var videoIds = new List<string>();
                var nextPageToken = "";
                var loaded = DateTime.UtcNow;

                while (nextPageToken != null)
                {
                    //see https://developers.google.com/youtube/v3/docs/playlistItems/list
                    var request = youtubeService.PlaylistItems.List("contentDetails");
                    // request.Key 
                    request.PlaylistId = command.PlaylistId;
                    request.MaxResults = command.Latest;
                    request.PageToken = nextPageToken;
                    // request.Key = youtubeService.ApiKey;

                    // Retrieve the list of videos uploaded to the authenticated user's channel.
                    var response = request.Execute();

                    foreach (var playlistItem in response.Items)
                    {
                        var captions = dataStore.GetAsync<string[]>(playlistItem.ContentDetails.VideoId).Result;
                        if (captions == null) captions = DownloadCaptions(playlistItem.ContentDetails.VideoId);

                        if (command.Terms.Any(t => captions.Any(c => c.Contains(t))))
                        {
                            videoIds.Add(playlistItem.ContentDetails.VideoId);
                            // Console.WriteLine("https://youtu.be/{0} ({1})", playlistItem.ContentDetails.VideoId, playlistItem.ContentDetails.VideoPublishedAt);
                        }
                    }

                    nextPageToken = response.NextPageToken;
                }

                cache = new PlaylistCache { Loaded = loaded, VideoIds = videoIds.ToArray() };
                dataStore.StoreAsync(command.PlaylistId, cache);
            }

            return cache.VideoIds;
        }

        private string[] DownloadCaptions(string videoId)
        {
            var captions = new List<string>();
            //see https://developers.google.com/youtube/v3/docs/captions/list
            var request = youtubeService.Captions.List(videoId, new[] { "id", "snippet" });
            // request.Key = youtubeService.ApiKey;
            var response = request.Execute();

            foreach (var item in response.Items)
            {
                /* download returns 403 Forbidden:
                    The permissions associated with the request are not sufficient to download the caption track.

                    "Got some feedback from the YouTube team, apparently the captions.download endpoint only works for videos your google account owns.
                    It is not usable for other videos." from https://stackoverflow.com/questions/30653865/downloading-captions-always-returns-a-403

                    Also no soultion in https://stackoverflow.com/questions/14061195/how-to-get-transcript-in-youtube-api-v3 */
                var download = youtubeService.Captions.Download(item.Id);
                // download.AccessToken = this.token;
                // download.Key = youtubeService.ApiKey;

                using (var stream = new MemoryStream())
                {
                    var state = download.DownloadWithStatus(stream);
                    stream.Seek(0, SeekOrigin.Begin);

                    using (var reader = new StreamReader(stream))
                    {
                        captions.Add(reader.ReadToEnd());
                    }
                }

                // var key = videoId + item.Id;
                // var caption = dataStore.GetAsync<string>(key).Result;

                // if (caption == null)
                // {
                //     //see https://developers.google.com/youtube/v3/docs/captions/download
                //     caption = youtubeService.Captions.Download(item.Id).Execute();
                //     dataStore.StoreAsync(key, caption);
                // }

                // captions.Add(caption);
            }

            return captions.ToArray();
        }

        private async Task<UserCredential> AuthorizeWithOAuthAsync()
        {
            UserCredential credential;

            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                      GoogleClientSecrets.Load(stream).Secrets,
                      // This OAuth 2.0 access scope allows for read-only access to the authenticated 
                      // user's account, but not other types of account access.
                      new[] { 
                        //   YouTubeService.Scope.YoutubeReadonly, 
                      YouTubeService.Scope.YoutubeForceSsl },
                      "user",
                      CancellationToken.None,
                      dataStore
                  );
            }

            this.token = credential.Token.AccessToken;

            return credential;
        }

        // private async Task Run()
        // {
        //     UserCredential credential = await AuthorizeWithOAuthAsync();

        //     var youtubeService = new YouTubeService(new BaseClientService.Initializer()
        //     {
        //         HttpClientInitializer = credential,
        //         ApplicationName = this.GetType().ToString()
        //     });

        //     var channelsListRequest = youtubeService.Channels.List("contentDetails");
        //     channelsListRequest.Mine = true;

        //     // Retrieve the contentDetails part of the channel resource for the authenticated user's channel.
        //     var channelsListResponse = await channelsListRequest.ExecuteAsync();

        //     foreach (var channel in channelsListResponse.Items)
        //     {
        //         // From the API response, extract the playlist ID that identifies the list
        //         // of videos uploaded to the authenticated user's channel.
        //         var uploadsListId = channel.ContentDetails.RelatedPlaylists.Uploads;

        //         Console.WriteLine("Videos in list {0}", uploadsListId);

        //         var nextPageToken = "";
        //         while (nextPageToken != null)
        //         {
        //             var playlistItemsListRequest = youtubeService.PlaylistItems.List("snippet");
        //             playlistItemsListRequest.PlaylistId = uploadsListId;
        //             playlistItemsListRequest.MaxResults = 50;
        //             playlistItemsListRequest.PageToken = nextPageToken;

        //             // Retrieve the list of videos uploaded to the authenticated user's channel.
        //             var playlistItemsListResponse = await playlistItemsListRequest.ExecuteAsync();

        //             foreach (var playlistItem in playlistItemsListResponse.Items)
        //             {
        //                 // Print information about each video.
        //                 Console.WriteLine("{0} ({1})", playlistItem.Snippet.Title, playlistItem.Snippet.ResourceId.VideoId);
        //             }

        //             nextPageToken = playlistItemsListResponse.NextPageToken;
        //         }
        //     }
        // }

        [Serializable]
        public class PlaylistCache
        {
            public DateTime Loaded { get; set; }
            public string[] VideoIds { get; internal set; }
        }
    }
}