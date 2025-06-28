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
        token.ThrowIfCancellationRequested();
        using var linkedTs = CancellationTokenSource.CreateLinkedTokenSource(token); // to cancel parallel searches on InputException
        var results = Channel.CreateUnbounded<VideoSearchResult>(new UnboundedChannelOptions() { SingleReader = true });
        ValueTask AddResult(VideoSearchResult r) => results.Writer.WriteAsync(r, linkedTs.Token);
        List<Task> searches = [];
        SearchPlaylistLikeScopes(command.Channels);
        SearchPlaylistLikeScopes(command.Playlists);

        if (command.HasValidVideos)
        {
            command.Videos!.ResetProgressAndNotifications(); // to prevent state from prior searches from bleeding into this one
            searches.Add(SearchVideosAsync(command, AddResult, linkedTs.Token));
        }

        var searching = Task.Run(async () =>
        {
            await foreach (var task in Task.WhenEach(searches))
            {
                if (task.IsFaulted)
                {
                    if (task.Exception.GetRootCauses().HaveInputError()) // displayed to the user, no need to record
                    {
                        /* wait for the root cause to bubble up instead of triggering
                         * an OperationCanceledException further up the call chain. */
                        linkedTs.Cancel(); // cancel parallel searches if query parser yields input error
                    }
                    //other exceptions are recorded in the scope and are not expected to bubble, see SearchUpdatingScope

                    throw task.Exception; // bubble up errors
                }
                // cancellation is recorded in the scope and not expected to throw or bubble, see SearchUpdatingScope
            }
        }, linkedTs.Token).ContinueWith(t =>
        {
            results.Writer.Complete(); // complete writer independent of cancellation to stop reader, which not guarded by it either
            if (t.IsFaulted) throw t.Exception; // bubble up errors
            // nothing to do if search is canceled
        });

        /* Determine whether the search spans multiple indexes, indicating that results have to be re-scored.
         * This is required because scores from different indexes are not comparable [cit. req.].
         *
         * Doing this before the search starts is not ideal because
         * a) SpansMultipleIndexShards may return a different value before and after playlist refresh
         * b) We can't know ahead of time whether the results will span multiple indexes as well and may rescore without having to.
         *
         * However, since this method yields results as soon as they're found and doesn't keep references to them,
         * we have to be pessimistic about rescoring. If we determined the number of distinct indexes that yielded results at runtime,
         * we wouldn't be able to rescore already yielded results if required. */
        var spansMultipleIndexes = command.GetScopes().Count() > 1
            || command.GetPlaylistLikeScopes().Any(pl => pl.SpansMultipleIndexShards());

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
                    searches.Add(SearchPlaylistAsync(command, scope, AddResult, linkedTs.Token));
                }
        }
    }

    private static async Task SearchUpdatingScope(Task searching, CommandScope scope, Action? cleanUp = null)
    {
        try
        {
            await searching; // to throw exceptions
            scope.Report(VideoList.Status.searched);
        }
        catch (Exception ex)
        {
            var causes = ex.GetRootCauses().ToArray();

            if (causes.AreAll<OperationCanceledException>()) scope.Report(VideoList.Status.canceled);
            else
            {
                scope.Notify("Errors searching", errors: [ex]);
                scope.Report(VideoList.Status.failed);
                if (causes.HaveInputError()) throw; // bubble up input errors to stop parallel scope searches
            }
        }
        finally { cleanUp?.Invoke(); }
    }

    private static string SelectUrl(IReadOnlyList<Thumbnail> thumbnails) => thumbnails.MinBy(tn => tn.Resolution.Area)!.Url;
}
