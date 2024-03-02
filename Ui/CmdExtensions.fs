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
