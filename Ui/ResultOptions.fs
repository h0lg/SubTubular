namespace Ui

open System
open Fabulous.Avalonia
open SubTubular
open type Fabulous.Avalonia.View

module ResultOptions =
    type Model =
        { OrderByScore: bool
          OrderDesc: bool
          Padding: int }

    type Msg =
        | OrderByScoreChanged of bool
        | OrderDescChanged of bool
        | PaddingChanged of float option

    let initModel =
        { OrderByScore = true
          OrderDesc = true
          Padding = 69 }

    let getSearchCommandOrderOptions model =
        match (model.OrderByScore, model.OrderDesc) with
        | (true, true) -> [ SearchCommand.OrderOptions.score ]
        | (true, false) -> [ SearchCommand.OrderOptions.score; SearchCommand.OrderOptions.asc ]
        | (false, true) -> [ SearchCommand.OrderOptions.uploaded ]
        | (false, false) -> [ SearchCommand.OrderOptions.uploaded; SearchCommand.OrderOptions.asc ]

    let orderVideoResults model videoResults =
        let sortBy =
            if model.OrderDesc then
                List.sortByDescending
            else
                List.sortBy

        let comparable: (VideoSearchResult -> IComparable) =
            if model.OrderByScore then _.Score else _.Video.Uploaded

        videoResults |> sortBy comparable

    let update msg model =
        match msg with
        | OrderByScoreChanged value -> { model with OrderByScore = value }
        | OrderDescChanged value -> { model with OrderDesc = value }

        | PaddingChanged padding ->
            { model with
                Padding = int padding.Value }

    let private getOrderDirection model =
        match (model.OrderByScore, model.OrderDesc) with
        | (true, true) -> "⋱ highest"
        | (true, false) -> "⋰ lowest"
        | (false, true) -> "⋱ latest"
        | (false, false) -> "⋰ earliest"

    let orderBy model =
        HStack(5) {
            Label "ordered by"

            ToggleButton(
                (if model.OrderByScore then "💯 score" else "📅 uploaded"),
                model.OrderByScore,
                OrderByScoreChanged
            )

            ToggleButton(getOrderDirection model, model.OrderDesc, OrderDescChanged)
            Label "first"
        }

    let padding model =
        HStack(5) {
            Label "padded with"

            NumericUpDown(0, float UInt16.MaxValue, Some(float model.Padding), PaddingChanged)
                .increment(5)
                .formatString("F0")
                .tooltip ("how much context to show a search result in")

            Label "chars for context"
        }
