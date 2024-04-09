namespace Ui

open System
open System.Threading
open Fabulous
open System.Runtime.CompilerServices

type DispatchExtensions =

    /// <summary>
    /// Creates a throttled dispatch factory that dispatches values in batches at a fixed minimum interval/maximum rate
    /// while ensuring that all values are dispatched eventually.
    /// This helps throttle the message dispatch of a rapid producer to avoid overloading the MVU loop
    /// without dropping any of the carried values - ensuring all values are processed in batches at a controlled rate.
    /// Note that this function creates an object with internal state and is intended to be used per Program
    /// or longer-running background process rather than once per message in the update function.
    /// </summary>
    /// <param name="interval">The minimum time interval between two consecutive dispatches in milliseconds.</param>
    /// <param name="mapBatchToMsg">A function that maps a list of pending input values to a message for dispatch.</param>
    /// <returns>
    /// Two functions. The first has a Dispatch signature and is used to feed a single value into the factory,
    /// where it is either dispatched immediately or after a delay respecting the interval,
    /// batched with other pending values in the order they were fed in.
    /// The second can be used for awaiting the next dispatch from the outside
    /// - while optionally adding some buffer time (in milliseconds) to account for race conditions.
    /// </returns>
    [<Extension>]
    static member batchThrottled((dispatch: Dispatch<'msg>), interval, (mapBatchToMsg: 'value list -> 'msg)) =
        let rateLimit = System.TimeSpan.FromMilliseconds(interval)
        let funLock = obj () // ensures safe access to resources shared across different threads
        let mutable lastDispatch = System.DateTime.MinValue
        let mutable pendingValues: 'value list = []
        let mutable cts: CancellationTokenSource = null // if set, allows canceling the last issued Command

        // gets the time to wait until the next allowed dispatch returning a negative timespan if the time is up
        let getTimeUntilNextDispatch () =
            lastDispatch.Add(rateLimit) - System.DateTime.UtcNow

        // dispatches all pendingValues and resets them while updating lastDispatch
        let dispatchBatch () =
            // Dispatch in the order they were received
            pendingValues |> List.rev |> mapBatchToMsg |> dispatch

            lastDispatch <- System.DateTime.UtcNow
            pendingValues <- []

        // a function with the Dispatch signature for feeding a single value into the throttled batch factory
        let dispatchSingle =
            fun (value: 'value) ->
                lock funLock (fun () ->
                    let untilNextDispatch = getTimeUntilNextDispatch ()
                    pendingValues <- value :: pendingValues

                    // If the interval has elapsed since the last dispatch, dispatch all pending messages
                    if untilNextDispatch <= System.TimeSpan.Zero then
                        dispatchBatch ()
                    else // schedule dispatch

                        // if the last sleeping dispatch can still be canceled, do so
                        if cts <> null then
                            cts.Cancel()
                            cts.Dispose()

                        // used to enable canceling this dispatch if newer values come into the factory
                        cts <- new CancellationTokenSource()

                        Async.Start(
                            async {
                                // wait only as long as we have to before next dispatch
                                do! Async.Sleep(untilNextDispatch)

                                lock funLock (fun () ->
                                    dispatchBatch ()

                                    // done; invalidate own cancellation
                                    if cts <> null then
                                        cts.Dispose()
                                        cts <- null)
                            },
                            cts.Token
                        ))

        // a function to wait until after the next async dispatch + some buffer time to ensure the dispatch is complete
        let awaitNextDispatch buffer =
            lock funLock (fun () ->
                async {
                    if not pendingValues.IsEmpty then
                        let untilAfterNextDispatch =
                            getTimeUntilNextDispatch ()
                            + match buffer with
                              | Some value -> System.TimeSpan.FromMilliseconds(value)
                              | None -> System.TimeSpan.Zero

                        if untilAfterNextDispatch > System.TimeSpan.Zero then
                            do! Async.Sleep(untilAfterNextDispatch)
                })

        // return both the dispatch and the await helper
        dispatchSingle, awaitNextDispatch

type AsyncEnumerableExtensions =

    [<Extension>]
    static member dispatchTo((this: Collections.Generic.IAsyncEnumerable<'result>), (dispatch: 'result -> unit)) =
        async {
            let results = this.GetAsyncEnumerator()

            let rec dispatchResults () =
                async {
                    let! hasNext = results.MoveNextAsync().AsTask() |> Async.AwaitTask

                    if hasNext then
                        results.Current |> dispatch
                        do! dispatchResults ()
                }

            do! dispatchResults ()
        }

    [<Extension>]
    static member dispatchBatchThrottledTo
        (
            (this: Collections.Generic.IAsyncEnumerable<'result>),
            throttleInterval,
            (mapPendingResultsToBatchMsg: 'result list -> 'msg),
            (dispatch: 'msg -> unit)
        ) =
        async {
            // create a throttled dispatch of a batch of pending results at regular intervals
            let dispatchSingleResult, awaitNextDispatch =
                dispatch.batchThrottled (throttleInterval, mapPendingResultsToBatchMsg)

            do! this.dispatchTo dispatchSingleResult // dispatch single results using throttled method
            do! awaitNextDispatch (Some throttleInterval) // to make sure all results are dispatched before calling it done
        }
