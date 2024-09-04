namespace Ui

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

            NumericUpDown(
                1,
                lastPage + 1 |> float,
                page + 1 |> float |> Some,
                fun p ->
                    goToPageMessage (
                        match p with
                        | Some p -> int p - 1
                        | None -> page
                    )
            )
                .formatString("F0")
                .tapCursor()
                .tooltip ("enter page, spin using mouse wheel, click or hold buttons")
        }
