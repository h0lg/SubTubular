namespace SubTubular.Gui

open System
open System.Linq
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open Fabulous
open FSharp.Control
open SubTubular
open SubTubular.Extensions

module OutputCommands =
    type Commands =
        | ListKeywords = 1
        | Search = 0

    type PagedKeywords =
        { Keywords: struct (string * int) array
          Page: uint16 }

    type Model =
        {
            Command: Commands
            Scopes: Scopes.Model
            ShowScopes: bool
            FocusQuery: bool
            Query: string

            ResultOptions: ResultOptions.Model

            Running: CancellationTokenSource
            SearchResults: (VideoSearchResult * uint32) list // result with applied Padding
            SearchResultPage: uint16

            /// video IDs by keyword by scope
            KeywordResults: Dictionary<CommandScope, Dictionary<string, List<string>>>
            DisplayedKeywords: Dictionary<CommandScope, PagedKeywords>

            DisplayOutputOptions: bool
            FileOutput: FileOutput.Model
        }

    type Msg =
        | CommandChanged of Commands
        | FocusQuery of bool
        | QueryChanged of string
        | ScopesMsg of Scopes.Msg
        | ShowScopesChanged of bool
        | CopyAsShellCmd
        | ResultOptionsMsg of ResultOptions.Msg
        | ResultOptionsChanged

        | DisplayOutputOptionsChanged of bool
        | FileOutputMsg of FileOutput.Msg
        | SavedOutput of string

        | Run of bool
        | CommandValidated of OutputCommand
        | SearchResults of VideoSearchResult list
        | GoToSearchResultPage of uint16
        | KeywordResults of (string array * string * CommandScope) list
        | GoToKeywordPage of (CommandScope * uint16)
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
                let allErrors = ConcurrentBag<string>()
                let command = mapToCommand model

                let join (lines: string seq) =
                    lines
                    |> Seq.filter (fun l -> l.IsNonEmpty())
                    |> String.concat ErrorLog.OutputSpacing

                try
                    let cancellation = model.Running.Token

                    // set up async notification channel
                    command.OnScopeNotification(fun scope title message errors ->
                        let lines = [ message; "in " + scope.Describe(false).Join(" ") ]

                        let msg =
                            match errors with
                            | [||] -> NotifyLong(title, join lines)
                            | _ ->
                                // collect error details for log
                                let errorDetails = errors |> Array.map _.ToString() |> List.ofArray
                                allErrors.Add(title :: lines @ errorDetails |> join)

                                // notify messages
                                let errorMsgs = errors |> Array.map _.Message
                                FailLong(title, Seq.append lines errorMsgs |> join)

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
                    let dispatchError (exn: exn) =
                        if exn :? InputException |> not then
                            allErrors.Add(exn.ToString())

                        Fail exn.Message |> dispatchCommon

                    match exn with
                    | :? OperationCanceledException -> Notify "The op was canceled" |> dispatchCommon
                    | :? AggregateException as exns ->
                        for inner in exns.Flatten().InnerExceptions do
                            dispatchError inner
                    | _ -> dispatchError exn

                if not allErrors.IsEmpty then
                    let! logWriting =
                        ErrorLog.WriteAsync(
                            allErrors |> Array.ofSeq |> join,
                            command.ToShellCommand(),
                            command.Describe(true)
                        )

                    let path, report = logWriting.ToTuple()

                    let title, body =
                        if path = null then
                            "Couldn't write an error log for this, sorry.", report
                        else
                            "Unexpected errors were caught and logged.",
                            "Find the following report in Storage > Go to Locations > errors:\n"
                            + IO.Path.GetFileName(path)

                    FailLong(title, body) |> dispatchCommon

                dispatch CommandCompleted
            }
            |> Async.AwaitTask
            |> Async.Start
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
                let counted = Youtube.CountKeywordVideos model.KeywordResults
                let! path = FileOutput.saveAsync listKeywords _.ListKeywords(counted)
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
          SearchResultPage = 0us
          KeywordResults = null
          DisplayedKeywords = null }

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
        | CopyAsShellCmd -> model, mapToCommand model |> CopyShellCmd |> Common |> Cmd.ofMsg

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
            if not on && model.Running <> null then
                if not model.Running.IsCancellationRequested then
                    model.Running.Cancel()

                model.Running.Dispose()

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

        | GoToSearchResultPage page -> { model with SearchResultPage = page }, Cmd.none

        | KeywordResults list ->
            for (keywords, videoId, scope) in list do
                Youtube.AggregateKeywords(keywords, videoId, scope, model.KeywordResults)

            let counted = Youtube.CountKeywordVideos model.KeywordResults

            { model with
                DisplayedKeywords = counted.ToDictionary(_.Key, (fun pair -> { Keywords = pair.Value; Page = 0us })) },
            Cmd.none

        | GoToKeywordPage(scope, page) ->
            model.DisplayedKeywords[scope] <-
                { model.DisplayedKeywords[scope] with
                    Page = page }

            model, Cmd.none

        | CommandCompleted ->
            if model.Running <> null then
                model.Running.Dispose()

            let cmd, body =
                if model.Command = Commands.Search then
                    let tracksWithErrors =
                        model.SearchResults
                        |> List.collect (fun r ->
                            let result, _ = r

                            result.Video.CaptionTracks
                            |> Seq.filter (fun t -> t.Error <> null)
                            |> List.ofSeq)

                    let body =
                        match tracksWithErrors with
                        | [] -> None
                        | list -> list.FormatErrors() |> Some

                    "search", body
                else
                    "listing keywords", None

            let title = cmd + " completed"

            let notify =
                match body with
                | Some b -> NotifyLong(title, b)
                | None -> Notify(title)

            { model with
                Running = null
                ShowScopes = false },
            notify |> Common |> Cmd.ofMsg

        | SearchResultMsg srm ->
            let cmd =
                match srm with
                | SearchResult.Msg.Common cmsg -> Common cmsg |> Cmd.ofMsg
                | _ -> Cmd.none

            model, cmd
