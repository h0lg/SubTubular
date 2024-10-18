namespace SubTubular.Gui

open System
open System.Collections.Generic
open System.Threading
open Avalonia.Controls
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia
open FSharp.Control
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module Search =
    type Commands =
        | ListKeywords = 1
        | Search = 0

    type Model =
        {
            Command: Commands
            Scopes: Scopes.Model
            ShowScopes: bool
            FocusQuery: bool
            Query: string

            ResultOptions: ResultOptions.Model

            Running: CancellationTokenSource
            SearchResults: (VideoSearchResult * uint32) list

            /// video IDs by keyword by scope
            KeywordResults: Dictionary<CommandScope, Dictionary<string, List<string>>>

            DisplayOutputOptions: bool
            FileOutput: FileOutput.Model
        }

    type Msg =
        | CommandChanged of Commands
        | FocusQuery of bool
        | QueryChanged of string
        | ScopesMsg of Scopes.Msg
        | ShowScopesChanged of bool
        | ResultOptionsMsg of ResultOptions.Msg
        | ResultOptionsChanged

        | DisplayOutputOptionsChanged of bool
        | FileOutputMsg of FileOutput.Msg
        | SavedOutput of string

        | Run of bool
        | CommandValidated of OutputCommand
        | SearchResults of VideoSearchResult list
        | KeywordResults of (string * string * CommandScope) list
        | CommandCompleted
        | SearchResultMsg of SearchResult.Msg
        | Common of CommonMsg

    let private mapToCommand model =
        let order = ResultOptions.getSearchCommandOrderOptions model.ResultOptions

        let command =
            if model.Command = Commands.Search then
                SearchCommand() :> OutputCommand
            else
                ListKeywords()

        Scopes.setOnCommand model.Scopes command

        match command with
        | :? SearchCommand as search ->
            search.Query <- model.Query
            search.Padding <- model.ResultOptions.Padding |> uint16
            search.OrderBy <- order
        | _ -> ()

        command.OutputHtml <- model.FileOutput.Html
        command.FileOutputPath <- model.FileOutput.To

        if model.FileOutput.Opening = FileOutput.Open.file then
            command.Show <- OutputCommand.Shows.file
        elif model.FileOutput.Opening = FileOutput.Open.folder then
            command.Show <- OutputCommand.Shows.folder

        command

    let private runCmd model =
        fun dispatch ->
            task {
                let dispatchCommon msg = Common msg |> dispatch

                try
                    let command = mapToCommand model
                    let cancellation = model.Running.Token

                    let join (lines: string list) =
                        lines |> List.filter (fun l -> l.IsNonEmpty()) |> String.concat "\n"

                    // set up async notification channel
                    command.OnScopeNotification(fun scope title message errors ->
                        let lines = [ "in " + scope.Describe(false).Join(" "); message ]

                        let msg =
                            match errors with
                            | [||] -> NotifyLong(title, join lines)
                            | _ ->
                                let errs = errors |> Array.map _.Message |> List.ofArray
                                FailLong(title, lines @ errs |> join)

                        dispatchCommon msg)

                    match command with
                    | :? SearchCommand as search ->
                        Prevalidate.Search search

                        if search.AreScopesValid() |> not then
                            do! RemoteValidate.ScopesAsync(search, Services.Youtube, Services.DataStore, cancellation)

                        if command.SaveAsRecent then
                            CommandValidated command |> dispatch

                        do!
                            Services.Youtube
                                .SearchAsync(search, cancellation)
                                .dispatchBatchThrottledTo (300, SearchResults, dispatch)

                    | :? ListKeywords as listKeywords ->
                        Prevalidate.Scopes listKeywords

                        if listKeywords.AreScopesValid() |> not then
                            do!
                                RemoteValidate.ScopesAsync(
                                    listKeywords,
                                    Services.Youtube,
                                    Services.DataStore,
                                    cancellation
                                )

                        if command.SaveAsRecent then
                            CommandValidated command |> dispatch

                        do!
                            Services.Youtube
                                .ListKeywordsAsync(listKeywords, cancellation)
                                .dispatchBatchThrottledTo (
                                    300,
                                    (fun list -> list |> List.map _.ToTuple() |> KeywordResults),
                                    dispatch
                                )

                    | _ -> failwith ("Unknown command type " + command.GetType().ToString())
                with exn ->
                    let dispatchError (exn: exn) = Fail exn.Message |> dispatchCommon

                    match exn with
                    | :? OperationCanceledException -> Notify "The op was canceled" |> dispatchCommon
                    | :? AggregateException as exns ->
                        for inner in exns.Flatten().InnerExceptions do
                            dispatchError inner
                    | _ -> dispatchError exn

                dispatch CommandCompleted
            }
            |> Async.AwaitTask
            |> Async.StartImmediate
        |> Cmd.ofEffect

    let private saveOutput model =
        async {
            let command = mapToCommand model

            match command with
            | :? SearchCommand as search ->
                Prevalidate.Search search

                let! path =
                    FileOutput.saveAsync search (fun writer ->
                        for result in model.SearchResults do
                            writer.WriteVideoResult(fst result, snd result))

                return SavedOutput path |> Some

            | :? ListKeywords as listKeywords ->
                Prevalidate.Scopes listKeywords
                let! path = FileOutput.saveAsync listKeywords _.ListKeywords(model.KeywordResults)
                return SavedOutput path |> Some

            | _ -> return None
        }
        |> Cmd.OfAsync.msgOption

    let initModel =
        { Command = Commands.Search
          Query = ""
          FocusQuery = false

          Scopes = Scopes.init ()
          ShowScopes = true
          ResultOptions = ResultOptions.initModel

          DisplayOutputOptions = false
          FileOutput = FileOutput.init ()

          Running = null
          SearchResults = []
          KeywordResults = null }

    let load (cmd: OutputCommand) model =
        let updated =
            match cmd with
            | :? SearchCommand as s ->
                { model with
                    Command = Commands.Search
                    Query = s.Query
                    ResultOptions =
                        { model.ResultOptions with
                            Padding = s.Padding |> int
                            OrderByScore = s.OrderBy |> Seq.contains SearchCommand.OrderOptions.score
                            OrderDesc = s.OrderBy |> Seq.contains SearchCommand.OrderOptions.asc |> not } }
            | :? ListKeywords as l ->
                { model with
                    Command = Commands.ListKeywords }
            | _ -> failwith "unsupported command type"

        let scopes, scopesCmd = Scopes.loadRecentCommand model.Scopes cmd

        { updated with
            Scopes = scopes
            ShowScopes = true
            SearchResults = []
            KeywordResults = null
            DisplayOutputOptions = false
            FileOutput =
                { updated.FileOutput with
                    To = cmd.FileOutputPath
                    Html = cmd.OutputHtml
                    Opening =
                        if cmd.Show.HasValue then
                            match cmd.Show.Value with
                            | OutputCommand.Shows.file -> FileOutput.Open.file
                            | OutputCommand.Shows.folder -> FileOutput.Open.folder
                            | _ -> FileOutput.Open.nothing
                        else
                            FileOutput.Open.nothing } },
        Cmd.map ScopesMsg scopesCmd

    let private applyResultOptions = Cmd.debounce 300 (fun () -> ResultOptionsChanged)
    let private getResults model = model.SearchResults |> List.map fst

    let private addPadding model list =
        list |> List.map (fun r -> r, uint32 model.ResultOptions.Padding)

    let update msg model =
        match msg with
        | CommandChanged cmd -> { model with Command = cmd }, Cmd.none
        | FocusQuery focus -> { model with FocusQuery = focus }, Cmd.none
        | QueryChanged txt -> { model with Query = txt }, Cmd.none

        | ScopesMsg scpsMsg ->
            let fwdCmd =
                match scpsMsg with
                | Scopes.Msg.Common cmsg -> Common cmsg |> Cmd.ofMsg
                | _ -> Cmd.none

            let scopes, cmd = Scopes.update scpsMsg model.Scopes
            { model with Scopes = scopes }, Cmd.batch [ fwdCmd; Cmd.map ScopesMsg cmd ]

        | ShowScopesChanged show -> { model with ShowScopes = show }, Cmd.none

        // udpate ResultOptions and debounce applying them to SearchResults
        | ResultOptionsMsg ext ->
            let options = ResultOptions.update ext model.ResultOptions
            { model with ResultOptions = options }, applyResultOptions ()

        // apply ResultOptions to SearchResults and debounce saving app settings
        | ResultOptionsChanged ->
            { model with
                SearchResults =
                    ResultOptions.orderVideoResults model.ResultOptions (getResults model)
                    |> addPadding model },
            Cmd.none

        | DisplayOutputOptionsChanged output ->
            { model with
                DisplayOutputOptions = output },
            Cmd.none

        | FileOutputMsg fom ->
            let updated =
                { model with
                    FileOutput = FileOutput.update fom model.FileOutput }

            let cmd =
                match fom with
                | FileOutput.Msg.SaveOutput -> saveOutput updated
                | _ -> Cmd.none

            updated, cmd

        | SavedOutput path -> model, Notify("Saved results to " + path) |> Common |> Cmd.ofMsg

        | Run on ->
            let updated =
                { model with
                    Running = if on then new CancellationTokenSource() else null

                    // reset result collections when starting a new search
                    SearchResults =
                        if on && model.Command = Commands.Search then
                            []
                        else
                            model.SearchResults
                    KeywordResults =
                        if on && model.Command = Commands.ListKeywords then
                            Dictionary<CommandScope, Dictionary<string, List<string>>>()
                        else
                            model.KeywordResults }

            updated, (if on then runCmd updated else Cmd.none)

        | CommandValidated _ -> model, Cmd.none
        | Common _ -> model, Cmd.none

        | SearchResults list ->
            { model with
                SearchResults =
                    getResults model @ list
                    |> ResultOptions.orderVideoResults model.ResultOptions
                    |> addPadding model },
            Cmd.none

        | KeywordResults list ->
            for (keyword, videoId, scope) in list do
                Youtube.AggregateKeywords(keyword, videoId, scope, model.KeywordResults)

            model, Cmd.none

        | CommandCompleted ->
            if model.Running <> null then
                model.Running.Dispose()

            let cmd =
                if model.Command = Commands.Search then
                    "search"
                else
                    "listing keywords"

            { model with
                Running = null
                ShowScopes = false },
            Notify(cmd + " completed") |> Common |> Cmd.ofMsg

        | SearchResultMsg srm ->
            let cmd =
                match srm with
                | SearchResult.Msg.Common cmsg -> Common cmsg |> Cmd.ofMsg
                | _ -> Cmd.none

            model, cmd

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

    let view model =
        (Grid(coldefs = [ Star ], rowdefs = [ Auto; Auto; Auto; Auto; Star ]) {
            let isSearch = model.Command = Commands.Search

            let hasResults =
                if isSearch then
                    not model.SearchResults.IsEmpty
                else
                    model.KeywordResults <> null && model.KeywordResults.Count > 0

            // scopes
            ScrollViewer(View.map ScopesMsg (Scopes.view model.Scopes)).card().isVisible (model.ShowScopes)

            // see https://usecasepod.github.io/posts/mvu-composition.html
            // and https://github.com/TimLariviere/FabulousContacts/blob/0d5024c4bfc7a84f02c0788a03f63ff946084c0b/FabulousContacts/ContactsListPage.fs#L89C17-L89C31
            // search options
            (Grid(coldefs = [ Auto; Star; Auto; Auto; Auto; Auto ], rowdefs = [ Auto ]) {
                Menu() {
                    MenuItem("🏷 List _keywords", CommandChanged Commands.ListKeywords)
                        .tooltip(ListKeywords.Description)
                        .tapCursor()
                        .asToggle (not isSearch)

                    MenuItem("🔍 _Search for", CommandChanged Commands.Search)
                        .tooltip(SearchCommand.Description)
                        .tapCursor()
                        .asToggle (isSearch)
                }

                TextBox(model.Query, QueryChanged)
                    .watermark("what to find")
                    .isVisible(isSearch)
                    .focus(model.FocusQuery)
                    .tooltip("focus using [Alt] + [F]")
                    .onLostFocus(fun _ -> FocusQuery false)
                    .gridColumn (1)

                TextBlock("ⓘ")
                    .attachedFlyout(queryFlyout ())
                    .tappable(ToggleFlyout >> Common, "read about the query syntax")
                    .fontSize(20)
                    .isVisible(isSearch)
                    .gridColumn (2)

                // invisible helper to focus query input
                Button("_focus Query", FocusQuery true).width (0)

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

                let isRunning = model.Running <> null

                ToggleButton((if isRunning then "✋ _Hold up!" else "👉 _Hit it!"), isRunning, Run)
                    .fontSize(16)
                    .margin(10, 0)
                    .gridColumn (5)
            })
                .card()
                .gridRow (1)

            // result options
            (Grid(coldefs = [ Auto; Star; Star; Auto ], rowdefs = [ Auto; Auto ]) {
                TextBlock("Results")

                (View.map ResultOptionsMsg (ResultOptions.orderBy model.ResultOptions))
                    .centerVertical()
                    .centerHorizontal()
                    .isVisible(isSearch)
                    .gridColumn (1)

                (View.map ResultOptionsMsg (ResultOptions.padding model.ResultOptions))
                    .centerHorizontal()
                    .isVisible(isSearch)
                    .gridColumn (2)

                ToggleButton("to file 📄", model.DisplayOutputOptions, DisplayOutputOptionsChanged).gridColumn (3)

                // output options
                (View.map FileOutputMsg (FileOutput.view model.FileOutput))
                    .isVisible(model.DisplayOutputOptions)
                    .gridRow(1)
                    .gridColumnSpan (4)
            })
                .card()
                .isVisible(hasResults)
                .gridRow (2)

            // results
            ScrollViewer(
                (VStack() {
                    if not isSearch && hasResults then
                        for scope in model.KeywordResults do
                            (VStack() {
                                TextBlock(scope.Key.Describe().Join(" "))

                                HWrap() {
                                    for keyword in Youtube.OrderKeywords scope.Value do
                                        TextBlock(keyword.Value.Count.ToString() + "x")

                                        Border(TextBlock(keyword.Key))
                                            .background(ThemeAware.With(Colors.Thistle, Colors.Purple))
                                            .cornerRadius(2)
                                            .padding(3, 0, 3, 0)
                                            .margin (3)
                                }
                            })
                                .trailingMargin ()

                    if isSearch && hasResults then
                        ListBox(
                            model.SearchResults,
                            (fun item ->
                                let result, padding = item
                                View.map SearchResultMsg (SearchResult.render padding result))
                        )
                })
            )
                .isVisible(hasResults)
                .card()
                .gridRow (4)
        })
            .margin (5, 5, 5, 0)
