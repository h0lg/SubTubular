namespace Ui

open System.Threading
open Fabulous

module CmdExtensions =
    /// <summary>
    /// Creates a Command factory that dispatches the most recent message in a given interval - even if delayed.
    /// This makes it similar to <see cref="throttle"/> in that it rate-limits the message dispatch
    /// and similar to <see cref="debounce"/> in that it guarantees the last message (within the interval or in total) is dispatched.
    /// Helpful for scenarios where you want to throttle, but cannot risk losing the last message to throttling
    /// - like the last progress update that completes a progress.
    /// Note that this function creates an object with internal state and is intended to be used per Program or longer-running background process
    /// rather than once per message in the update function.
    /// </summary>
    /// <param name="interval">The minimum time interval between two consecutive Command executions in milliseconds.</param>
    /// <param name="fn">A function that maps a factory input value to a message for dispatch.</param>
    /// <returns>
    /// A Command factory function that maps an input value to a "buffered throttle" Command which dispatches the most recent message (mapped from the value)
    /// if the minimum time interval has elapsed since the last Command execution; otherwise, it does nothing.
    /// </returns>
    let bufferedThrottle (interval: int) (fn: 'value -> 'msg) : 'value -> Cmd<'msg> =
        let funLock = obj () // ensures safe access to resources shared across different threads
        let mutable lastDispatch = System.DateTime.MinValue
        let mutable cts: CancellationTokenSource = null // if set, allows cancelling the last issued Command

        // Return a factory function mapping input values to buffered throttled Commands with delayed dispatch of the most recent message
        fun (value: 'value) ->
            [ fun dispatch ->
                  // Lock to ensure thread-safe access to shared resources
                  lock funLock (fun () ->
                      let now = System.DateTime.UtcNow
                      let elapsedSinceLastDispatch = now - lastDispatch
                      let rateLimit = System.TimeSpan.FromMilliseconds(float interval)

                      // If the interval has elapsed since the last dispatch, dispatch immediately
                      if elapsedSinceLastDispatch >= rateLimit then
                          lastDispatch <- now
                          dispatch (fn value)
                      else // schedule the dispatch for when the interval is up
                          // cancel the last sleeping Command issued earlier from this factory
                          if cts <> null then
                              cts.Cancel()
                              cts.Dispose()

                          // make cancellation available to the factory's next Command
                          cts <- new CancellationTokenSource()

                          // asynchronously wait for the remaining time before dispatch
                          Async.Start(
                              async {
                                  do! Async.Sleep(rateLimit - elapsedSinceLastDispatch)

                                  lock funLock (fun () ->
                                      dispatch (fn value)

                                      // done; invalidate own cancellation token
                                      if cts <> null then
                                          cts.Dispose()
                                          cts <- null)
                              },
                              cts.Token
                          )) ]

    /// <summary>
    /// Creates a factory for Commands that dispatch messages with a list of pending values at a fixed maximum rate,
    /// ensuring that all pending values are dispatched when the specified interval elapses.
    /// This function is similar to <see cref="bufferedThrottle"/>, but instead of dispatching only the last value,
    /// it remembers and dispatches all undispatched values within the specified interval.
    /// Helpful for scenarios where you want to throttle messages but cannot afford to lose any of the values they carry,
    /// ensuring all values are processed at a controlled rate.
    /// Note that this function creates an object with internal state and is intended to be used per Program
    /// or longer-running background process rather than once per message in the update function.
    /// </summary>
    /// <param name="interval">The minimum time interval between two consecutive Command executions in milliseconds.</param>
    /// <param name="fn">A function that maps a list of factory input values to a message for dispatch.</param>
    /// <returns>
    /// Two methods - the first being a Command factory function that maps a list of input values to a Command
    /// which dispatches a message (mapped from the pending values),
    /// either immediately or after a delay respecting the interval, while remembering and dispatching all remembered values
    /// when the interval has elapsed, ensuring no values are lost.
    /// The second can be used for awaiting the next dispatch from the outside while adding some buffer time.
    /// </returns>
    let batchedThrottle
        (interval: int)
        (mapValuesToMsg: 'value list -> 'msg)
        : ('value -> Cmd<'msg>) * (System.TimeSpan option -> Async<unit>) =
        let rateLimit = System.TimeSpan.FromMilliseconds(float interval)
        let funLock = obj () // ensures safe access to resources shared across different threads
        let mutable lastDispatch = System.DateTime.MinValue
        let mutable pendingValues: 'value list = []
        let mutable cts: CancellationTokenSource = null // if set, allows cancelling the last issued Command

        // gets the time to wait until the next allowed dispatch returning a negative timespan if the time is up
        let getTimeUntilNextDispatch () =
            lastDispatch.Add(rateLimit) - System.DateTime.UtcNow

        // dispatches all pendingValues and resets them while updating lastDispatch
        let dispatchBatch (dispatch: 'msg -> unit) =
            // Dispatch in the order they were received
            pendingValues |> List.rev |> mapValuesToMsg |> dispatch

            lastDispatch <- System.DateTime.UtcNow
            pendingValues <- []

        // a factory function mapping input values to sleeping Commands dispatching all pending messages
        let factory =
            fun (value: 'value) ->
                [ fun dispatch ->
                      lock funLock (fun () ->
                          let untilNextDispatch = getTimeUntilNextDispatch ()
                          pendingValues <- value :: pendingValues

                          // If the interval has elapsed since the last dispatch, dispatch all pending messages
                          if untilNextDispatch <= System.TimeSpan.Zero then
                              dispatchBatch dispatch
                          else // schedule dispatch

                              // if the the last sleeping dispatch can still be cancelled, do so
                              if cts <> null then
                                  cts.Cancel()
                                  cts.Dispose()

                              // used to enable cancelling this dispatch if newer values come into the factory
                              cts <- new CancellationTokenSource()

                              Async.Start(
                                  async {
                                      // wait only as long as we have to before next dispatch
                                      do! Async.Sleep(untilNextDispatch)

                                      lock funLock (fun () ->
                                          dispatchBatch dispatch

                                          // done; invalidate own cancellation
                                          if cts <> null then
                                              cts.Dispose()
                                              cts <- null)
                                  },
                                  cts.Token
                              )) ]

        // a function to wait until after the next async dispatch + some buffer time to ensure the dispatch is complete
        let awaitNextDispatch buffer =
            lock funLock (fun () ->
                async {
                    if not pendingValues.IsEmpty then
                        let untilAfterNextDispatch =
                            getTimeUntilNextDispatch ()
                            + match buffer with
                              | Some value -> value
                              | None -> System.TimeSpan.Zero

                        if untilAfterNextDispatch > System.TimeSpan.Zero then
                            do! Async.Sleep(untilAfterNextDispatch)
                })

        // return both the factory and the await helper
        factory, awaitNextDispatch
