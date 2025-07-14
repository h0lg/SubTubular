﻿namespace SubTubular.Gui

open System
open Avalonia.Controls
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View
open OutputCommands

module OutputCommandView =
    let private queryFlyout () =
        Flyout(
            (VStack() {
                TextBlock("The full-text search is powered by LIFTI.").margin (0, 0, 0, 10)

                for hint in SearchCommand.QueryHints do
                    TextBlock("▪ " + hint).wrap ()

                TextBlock("Read more about the syntax online 📡")
                    .background(ThemeAware.With(Colors.SkyBlue, Colors.Purple))
                    .margin(0, 10, 0, 0)
                    .right()
                    .tappable (
                        Common(OpenUrl "https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/"),
                        "Open the LIFTI query syntax help page in your browser"
                    )
            })
                .maxWidth (400)
        )
            .placement(PlacementMode.BottomEdgeAlignedRight)
            .showMode (FlyoutShowMode.Standard)
    //.placement (PlacementMode.RightEdgeAlignedTop)

    let private renderKeyWordPage scope pagedKeywords =
        let keywordsOnPage, pager =
            Pager.render pagedKeywords.Keywords (int pagedKeywords.Page) 100 (fun page ->
                GoToKeywordPage(scope, uint16 page))

        (Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto; Auto ]) {
            TextBlock(scope.Describe().Join(" ")).wrap().header ()
            pager.bottom().gridColumn (1)

            (HWrap() {
                for pair in keywordsOnPage do
                    let keyword, videoCount = pair.ToTuple()
                    TextBlock(videoCount.ToString() + "x")

                    Border(TextBlock(keyword))
                        .background(ThemeAware.With(Colors.Thistle, Colors.Purple))
                        .cornerRadius(2)
                        .padding(3, 0, 3, 0)
                        .margin (3)
            })
                .gridRow(1)
                .gridColumnSpan (2)
        })
            .trailingMargin ()

    let private renderCommandOptions model isSearch =
        Grid(coldefs = [ Auto; Star; Auto; Auto; Auto; Auto; Auto ], rowdefs = [ Auto ]) {
            Menu() {
                MenuItem("🏷 List _keywords")
                    .onClick(fun _ -> CommandChanged Commands.ListKeywords)
                    .tooltip(ListKeywords.Description)
                    .tapCursor()
                    .asToggle (not isSearch)

                MenuItem(Icon.search + "_Search for")
                    .onClick(fun _ -> CommandChanged Commands.Search)
                    .tooltip(SearchCommand.Description)
                    .tapCursor()
                    .asToggle (isSearch)
            }

            TextBox(model.Query, QueryChanged)
                .watermark("what to find")
                .isVisible(isSearch)
                .multiline(true)
                .focus(model.FocusQuery)
                .tooltip("focus using [Alt] + [F]")
                .onLostFocus(fun _ -> FocusQuery false)
                .gridColumn (1)

            // invisible helper to focus query input
            Button("_focus Query", FocusQuery true).width (0)

            TextBlock(Icon.help)
                .attachedFlyout(queryFlyout ())
                .tappable(ToggleFlyout >> Common, "read about the query syntax")
                .padding(5, -3)
                .fontSize(25)
                .isVisible(isSearch)
                .gridColumn (2)

            Label("in").centerVertical().gridColumn (3)

            ToggleButton(
                (if model.ShowScopes then
                     "👆 these scopes"
                 else
                     $"✊ {model.Scopes.List.Length} scopes"),
                model.ShowScopes,
                ShowScopesChanged
            )
                .gridColumn (4)

            Button(Icon.copy, CopyAsShellCmd).tooltip("copy shell command to clipboard").gridColumn (5)

            let isRunning = model.Running <> null

            ToggleButton((if isRunning then "✋ _Hold up!" else "👉 _Hit it!"), isRunning, Run)
                .fontSize(16)
                .margin(10, 0)
                .gridColumn (6)
        }

    let private renderResultOptions model isSearch (resultPager: WidgetBuilder<Msg, IFabReversibleStackPanel> option) =
        Grid(coldefs = [ Auto; Star; Star; Star; Auto ], rowdefs = [ Auto; Auto ]) {
            TextBlock("Results").header ()

            (View.map ResultOptionsMsg (ResultOptions.orderBy model.ResultOptions))
                .centerVertical()
                .centerHorizontal()
                .isVisible(isSearch)
                .gridColumn (1)

            (View.map ResultOptionsMsg (ResultOptions.padding model.ResultOptions))
                .centerHorizontal()
                .isVisible(isSearch)
                .gridColumn (2)

            if resultPager.IsSome then
                resultPager.Value.gridColumn (3)

            ToggleButton("to file 📄", model.DisplayOutputOptions, DisplayOutputOptionsChanged).gridColumn (4)

            // output options
            (View.map FileOutputMsg (FileOutput.view model.FileOutput))
                .isVisible(model.DisplayOutputOptions)
                .gridRow(1)
                .gridColumnSpan (5)
        }

    let render model showThumbnails =
        (Grid(coldefs = [ Star ], rowdefs = [ Auto; Auto; Auto; Auto; Star ]) {
            let isSearch = model.Command = Commands.Search

            let hasResults =
                if isSearch then
                    not model.SearchResults.IsEmpty
                else
                    model.DisplayedKeywords <> null && model.DisplayedKeywords.Count > 0

            let searchResultsOnPage, resultPager =
                match isSearch && hasResults with
                | true ->
                    let page, pager =
                        Pager.render (model.SearchResults |> Array.ofList) (int model.SearchResultPage) 10 (fun page ->
                            GoToSearchResultPage(uint16 page))

                    Some page, Some pager
                | false -> None, None

            // scopes
            ScrollViewer(View.map ScopesMsg (Scopes.view model.Scopes showThumbnails))
                .card()
                .isVisible (model.ShowScopes)

            // command options
            (renderCommandOptions model isSearch).card().gridRow (1)

            // result options
            (renderResultOptions model isSearch resultPager).card().isVisible(hasResults).gridRow (2)

            // results
            ScrollViewer(
                (VStack() {
                    if not isSearch && hasResults then
                        for scope in model.DisplayedKeywords do
                            renderKeyWordPage scope.Key scope.Value

                    if searchResultsOnPage.IsSome then
                        ListBox(
                            searchResultsOnPage.Value,
                            (fun item ->
                                let result, padding = item
                                View.map SearchResultMsg (SearchResult.render padding result showThumbnails))
                        )
                })
            )
                .isVisible(hasResults)
                .card()
                .gridRow (4)
        })
            .margin (5, 5, 5, 0)
