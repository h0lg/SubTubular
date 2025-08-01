﻿namespace SubTubular.Gui

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
        | SaveRecent of OutputCommand
        | SearchResults of VideoSearchResult list
        | GoToSearchResultPage of uint16
        | KeywordResults of (string array * string * CommandScope) list
        | GoToKeywordPage of (CommandScope * uint16)
        | CommandCompleted of bool
        | SearchResultMsg of SearchResult.Msg
        | Common of CommonMsg

    let private mapToCommand model includeOutputOptions =
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

        if includeOutputOptions then
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
                let loggedErrors = ConcurrentBag<string>()
                let command = mapToCommand model false
                let token = model.Running.Token

                command.OnScopeNotification(fun scope ntf ->
                    if ntf.Errors.HasAny() then
                        let causes = ntf.Errors.GetRootCauses().ToArray()

                        if causes.AnyNeedReporting() then
                            loggedErrors.Add(
                                causes
                                    .Select(fun ex -> ex.ToString())
                                    .Prepend("in " + scope.Describe(false).Join(" "))
                                    .Prepend($"{DateTime.Now:O} {ntf.Title}")
                                    .Join("\n")
                            ))

                try
                    match command with
                    | :? SearchCommand as search ->
                        Prevalidate.Search search

                        if search.RequiresRemoteValidation() then
                            do! RemoteValidate.ScopesAsync(search, Services.Youtube, Services.DataStore, token)

                        if command.SaveAsRecent then
                            SaveRecent command |> dispatch

                        do!
                            Services.Youtube
                                .SearchAsync(search, token)
                                .dispatchBatchThrottledTo (300, SearchResults, dispatch)

                    | :? ListKeywords as listKeywords ->
                        Prevalidate.Scopes listKeywords

                        if listKeywords.RequiresRemoteValidation() then
                            do! RemoteValidate.ScopesAsync(listKeywords, Services.Youtube, Services.DataStore, token)

                        if command.SaveAsRecent then
                            SaveRecent command |> dispatch

                        do!
                            Services.Youtube
                                .ListKeywordsAsync(listKeywords, token)
                                .dispatchBatchThrottledTo (
                                    300,
                                    (fun list -> list |> List.map _.ToTuple() |> KeywordResults),
                                    dispatch
                                )

                    | _ -> failwith ("Unknown command type " + command.GetType().ToString())
                with exn ->
                    let causes = exn.GetRootCauses().ToArray()

                    if causes.AnyNeedReporting() then
                        loggedErrors.Add($"{DateTime.Now:O} {exn}")

                let failedHard = not loggedErrors.IsEmpty

                if failedHard then
                    let! logWriting =
                        ErrorLog.WriteAsync(
                            loggedErrors.Join(ErrorLog.OutputSpacing),
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

                let failed =
                    failedHard || command.GetScopes().Any(fun s -> s.Notifications.HaveErrors())

                (*  dispatch if search completes without user cancellation
                    while dispatching input error despite internal cancellation *)
                if failed || not token.IsCancellationRequested then
                    failed |> not |> CommandCompleted |> dispatch
            }
            |> Async.AwaitTask
            |> Async.Start
        |> Cmd.ofEffect

    let private saveOutput model =
        fun dispatch ->
            async {
                match mapToCommand model true with
                | :? SearchCommand as search ->
                    Prevalidate.Search search

                    let! path =
                        FileOutput.saveAsync search (fun writer ->
                            for result in model.SearchResults do
                                writer.WriteVideoResult(fst result, snd result))

                    SavedOutput path |> dispatch

                    if search.SaveAsRecent then
                        SaveRecent search |> dispatch

                | :? ListKeywords as listKeywords ->
                    Prevalidate.Scopes listKeywords
                    let counted = Youtube.CountKeywordVideos model.KeywordResults
                    let! path = FileOutput.saveAsync listKeywords _.ListKeywords(counted)
                    SavedOutput path |> dispatch

                    if listKeywords.SaveAsRecent then
                        SaveRecent listKeywords |> dispatch

                | _ -> ()
            }
            |> Async.Start
        |> Cmd.ofEffect

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

            (*  override file output settings with persisted values
                to prevent current values from bleeding into the loaded command *)
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
        | CopyAsShellCmd -> model, mapToCommand model true |> CopyShellCmd |> Common |> Cmd.ofMsg

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
            let fileOutput, fwdCmd = FileOutput.update fom model.FileOutput
            let model = { model with FileOutput = fileOutput }

            let saving =
                match fom with
                | FileOutput.Msg.SaveOutput -> saveOutput model
                | _ -> Cmd.none

            model, Cmd.batch [ saving; Cmd.map FileOutputMsg fwdCmd ]

        | SavedOutput path -> model, Notify("Saved results to " + path) |> Common |> Cmd.ofMsg

        | Run on ->
            if not on && model.Running <> null then
                if not model.Running.IsCancellationRequested then
                    model.Running.Cancel()

                model.Running.Dispose() // explicitly, before setting it null below to avoid waiting for GC

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

        | SaveRecent _ -> model, Cmd.none
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

        | CommandCompleted successful ->
            if model.Running <> null then // not canceled
                model.Running.Dispose() // explicitly, before setting it null below to avoid waiting for GC

            let workload =
                if model.Command = Commands.Search then
                    "search"
                else
                    "listing keywords"

            let msg =
                if successful then
                    Notify(workload + " completed.")
                else
                    FailLong(workload + " failed.", "Find details in the scope notifications.")

            { model with
                Running = null

                // hide scopes if none has errors or warnings
                ShowScopes =
                    model.Scopes.List
                    |> List.exists (fun scope -> scope.Notifications.HasWarnings || scope.Notifications.HasError) },
            msg |> Common |> Cmd.ofMsg

        | SearchResultMsg srm ->
            let cmd =
                match srm with
                | SearchResult.Msg.Common cmsg -> Common cmsg |> Cmd.ofMsg
                | _ -> Cmd.none

            model, cmd
