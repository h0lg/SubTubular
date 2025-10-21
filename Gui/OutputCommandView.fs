namespace SubTubular.Gui

open System
open Avalonia
open Avalonia.Controls
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
                    .classes("external-link")
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

                    Border(TextBlock(keyword)).classes ("keyword")
            })
                .gridRow(1)
                .gridColumnSpan (2)
        })
            .trailingMargin ()

    let private renderCommandOptions model isSearch =
        Grid(coldefs = [ Auto; Star; Auto; Auto; Auto; Auto; Auto ], rowdefs = [ Auto ]) {
            Menu() {
                MenuItem("🏷 List _keywords")
                    .onClick(fun _ -> CommandChanged Keywords)
                    .tooltip(ListKeywords.Description)
                    .tapCursor()
                    .asToggle (not isSearch)

                MenuItem(Icon.search + "_Search for")
                    .onClick(fun _ -> CommandChanged Search)
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

    let private getScopesHeight model hasResults =
        if model.ShowScopes then
            let max = model.MaxSize.Height // start with full page height
            // use only half if we're also displaying results, otherwise full minus some padding for other controls
            let max = if hasResults then max / float 2 else max - float 60
            let max = if max < 0 then float 0 else max // make sure max is positive or 0
            let desired = model.Scopes.ListHeight + float 54 // makes up for add buttons
            let height = if max < desired then max else desired // take up the required or max allowed height
            Pixel height // Star or Pixel Height allows ScrollViewer to work by limiting its height
        else
            Auto // hidden, take up no space

    let render model showThumbnails =
        let isSearch = model.Command = Search

        let hasResults =
            match model.Command with
            | Search -> not model.SearchResults.IsEmpty
            | Keywords -> model.DisplayedKeywords <> null && model.DisplayedKeywords.Count > 0

        let searchResultsOnPage, resultPager =
            if isSearch && hasResults then
                let page, pager =
                    Pager.render (model.SearchResults |> Array.ofList) (int model.SearchResultPage) 10 (fun page ->
                        GoToSearchResultPage(uint16 page))

                Some page, Some pager
            else
                None, None

        let scopesHeight = getScopesHeight model hasResults

        (Grid(coldefs = [ Star ], rowdefs = [ scopesHeight; Auto; Auto; Star ]) {
            // scopes
            (Panel() {
                ScrollViewer(View.map ScopesMsg (Scopes.list model.Scopes model.MaxSize.Width showThumbnails))
                    .margin(0, 0, 0, 20)
                    .card ()

                (View.map ScopesMsg (Scopes.addButtons model.Scopes)).margin (0, 0, 10, -10)
            })
                .margin(0, 0, 0, 15)
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
                .card()
                .isVisible(hasResults)
                .gridRow (3)
        })
            .onSizeChanged(ContainerSizeChanged)
            .margin (5, 5, 5, 0)
