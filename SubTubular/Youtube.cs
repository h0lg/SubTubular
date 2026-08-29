using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SubTubular.Extensions;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace SubTubular;

public sealed partial class Youtube(DataStore dataStore, VideoIndexRepository videoIndexRepo) : IDisposable
{
    private readonly YoutubeClient client = new();

    public async IAsyncEnumerable<VideoSearchResult> SearchAsync(SearchCommand command,
        [EnumeratorCancellation] CancellationToken token = default)
    {
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
            await foreach (var task in Task.WhenEach(searches).ContinueAnywhere())
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
        }, CancellationToken.None); // let writer complete

        /* Determine whether the search spans multiple indexes, indicating that results have to be re-scored.
         * This is required because scores from different indexes are not comparable [cit. req.].
         *
         * Doing this before the search starts is not ideal because
         * a) SpansMultipleIndexShards may return a different value before and after playlist refresh
         * b) We can't know ahead of time whether the results will span multiple indexes as well and may rescore without having to.
         *
         * However, since this method yields results as soon as they're found and doesn't keep references to them,
         * we have to be pessimistic about re-scoring. If we determined the number of distinct indexes that yielded results at runtime,
         * we wouldn't be able to rescore already yielded results if required. */
        var spansMultipleIndexes = command.SpansMultipleIndexes();

        // don't pass cancellation token to avoid throwing before searching is awaited below
        await foreach (var result in results.Reader.ReadAllAsync(CancellationToken.None).ContinueAnywhere())
        {
            if (linkedTs.Token.IsCancellationRequested) break; // end loop gracefully to throw below
            if (spansMultipleIndexes) result.Rescore();
            yield return result;
        }

        if (spansMultipleIndexes != command.SpansMultipleIndexes())
            command.Notify(spansMultipleIndexes ? "Result scores can be improved" : "Result scores are inaccurate",
                spansMultipleIndexes ? "The search unexpectedly ran on a single index. If you repeat it, that may improve ordering by score."
                    : "The search unexpectedly ran on multiple indexes, turning the result scores calculated for one index stale."
                        + " If you repeat it, results will be re-scored across multiple scopes using a simplified algorithm.");

        await searching.ContinueAnywhere(); // throws the relevant input errors

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

    /// <summary>Awaits the <paramref name="search"/> task and updates the <paramref name="scope"/>
    /// with a <see cref="VideoList.Status"/> according to its outcome.
    /// It catches aggregated exceptions and notifies the <paramref name="scope"/> about them,
    /// only bubbling up those that <see cref="ExceptionExtensions.HaveInputError(IEnumerable{Exception})"/>
    /// so they can trigger the cancellation of parallel searches in
    /// <see cref="SearchAsync(SearchCommand, CancellationToken, CancellationTokenSource?)"/>.</summary>
    /// <param name="cleanUp">An optional action called after the <paramref name="search"/>
    /// has completed to free resources used by it.</param>
    private static async Task SearchUpdatingScope(Task search, CommandScope scope, Action? cleanUp = null)
    {
        try
        {
            await search.ContinueAnywhere(); // to throw exceptions
            scope.Report(VideoList.Status.searched);
        }
        catch (Exception ex)
        {
            var causes = ex.GetRootCauses().ToArray();

            if (causes.AreAllCancelations()) scope.Report(VideoList.Status.canceled);
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

    public void Dispose() => client.Dispose();
}
