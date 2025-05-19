using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SubTubular.Extensions;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace SubTubular;

public sealed partial class Youtube(DataStore dataStore, VideoIndexRepository videoIndexRepo)
{
    public readonly YoutubeClient Client = new();

    public async IAsyncEnumerable<VideoSearchResult> SearchAsync(SearchCommand command,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        using var linkedTs = CancellationTokenSource.CreateLinkedTokenSource(token); // to cancel parallel searches on InputException
        var results = Channel.CreateUnbounded<VideoSearchResult>(new UnboundedChannelOptions() { SingleReader = true });
        Func<VideoSearchResult, ValueTask> addResult = r => results.Writer.WriteAsync(r, linkedTs.Token);
        List<Task> searches = [];
        SearchPlaylistLikeScopes(command.Channels);
        SearchPlaylistLikeScopes(command.Playlists);

        if (command.HasValidVideos)
        {
            command.Videos!.ResetProgressAndNotifications();
            searches.Add(SearchVideosAsync(command, addResult, linkedTs.Token));
        }

        var spansMultipleIndexes = searches.Count > 0;

        var searching = Task.Run(async () =>
        {
            await foreach (var task in Task.WhenEach(searches))
            {
                if (task.IsFaulted)
                {
                    if (task.Exception.HasInputRootCause())
                    {
                        /* wait for the root cause to bubble up instead of triggering
                         * an OperationCanceledException further up the call chain. */
                        linkedTs.Cancel(); // cancel parallel searches if query parser yields input error
                    }

                    throw task.Exception; // bubble up errors
                }
            }
        }, linkedTs.Token).ContinueWith(t =>
        {
            results.Writer.Complete();
            if (t.IsFaulted) throw t.Exception; // bubble up errors
        });

        // don't pass cancellation token to avoid throwing before searching is awaited below
        await foreach (var result in results.Reader.ReadAllAsync())
        {
            if (linkedTs.Token.IsCancellationRequested) break; // end loop gracefully to throw below
            if (spansMultipleIndexes) result.Rescore();
            yield return result;
        }

        await searching; // throws the relevant input errors

        void SearchPlaylistLikeScopes(PlaylistLikeScope[]? scopes)
        {
            if (scopes.HasAny())
                foreach (var scope in scopes!)
                {
                    scope.ResetProgressAndNotifications(); // to prevent state from prior searches from bleeding into this one
                    searches.Add(SearchPlaylistAsync(command, scope, addResult, linkedTs.Token));
                }
        }
    }

    private static string SelectUrl(IReadOnlyList<Thumbnail> thumbnails) => thumbnails.MinBy(tn => tn.Resolution.Area)!.Url;
}