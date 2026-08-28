namespace SubTubular.Gui

open System
open System.Runtime.CompilerServices
open Fabulous

[<AutoOpen>]
module Helpers =
    let (|Default|) defaultValue input = defaultArg input defaultValue

module Dispatch =

    let toUiThread (action: unit -> unit) =
        // Check if the current thread is the UI thread
        if Avalonia.Threading.Dispatcher.UIThread.CheckAccess() then
            action () // run action on current thread
        else
            // If not on the UI thread, invoke the code on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.Invoke(action)

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
    static member dispatchBuffered
        (
            (this: Collections.Generic.IAsyncEnumerable<'result>),
            throttleInterval: int64,
            (mapPendingResultsToBatchMsg: 'result list -> 'msg),
            (dispatch: 'msg -> unit)
        ) =
        task {
            // create a throttled dispatch of a batch of pending results at regular intervals
            let throttle =
                Dispatch.batchThrottled
                    (TimeSpan.FromMilliseconds(throttleInterval))
                    mapPendingResultsToBatchMsg
                    dispatch

            try
                do! this.dispatchTo throttle.Dispatch // dispatch single results using throttled method
                do! throttle.FlushAsync()
            finally
                throttle.Dispose()
        }
