namespace SubTubular.Gui

open Fabulous.Avalonia
open type Fabulous.Avalonia.View

module Pager =
    let render (results: 'T array) page pageSize goToPageMessage =
        let lastPage = results.Length / pageSize
        let offset = pageSize * page
        let resultsOnPage = results |> Array.skip offset |> Array.truncate pageSize

        resultsOnPage,
        HStack() {
            Label("page")

            let msg =
                fun p ->
                    goToPageMessage (
                        match p with
                        | Some p -> int p - 1
                        | None -> page
                    )

            (intUpDown 1 (page + 1 |> float) (lastPage + 1 |> float) msg "select the page to display").tapCursor ()
        }
