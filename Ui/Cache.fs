namespace Ui

open System
open System.IO
open Avalonia.Layout
open Fabulous
open Fabulous.Avalonia
open SubTubular

open type Fabulous.Avalonia.View

module Cache =
    type LastAccessGroup = { Name: string; Files: FileInfo list }

    type Model =
        { LastAccessGroups: LastAccessGroup list option }

    type Msg =
        | ExpandingCacheByLastAccess
        | Remove of LastAccessGroup

    // Function to describe a TimeSpan into specific ranges
    let private describeTimeSpan (timeSpan: TimeSpan) =
        match timeSpan with
        | ts when ts < TimeSpan.FromDays(1.0) -> "day"
        | ts when ts < TimeSpan.FromDays(7.0) -> sprintf "%d days" (ts.Days + 1)
        | ts when ts < TimeSpan.FromDays(30.0) ->
            let weeks = ts.Days / 7

            if weeks = 0 then "week" else sprintf "%d weeks" (weeks + 1)
        | ts when ts < TimeSpan.FromDays(90.0) -> // 90 days for roughly 3 months (quarter year)
            let months = ts.Days / 30

            if months = 0 then
                "month"
            else
                sprintf "%d months" (months + 1)
        | _ -> "eon"

    let private loadByLastAccess () =
        let now = DateTime.Now

        FileHelper.EnumerateFiles(Services.CacheFolder, "*", SearchOption.AllDirectories)
        |> Seq.sortByDescending _.LastAccessTime // latest first
        |> Seq.groupBy (fun f -> now - f.LastAccessTime |> describeTimeSpan)
        |> Seq.map (fun group ->
            { Name = fst group
              Files = snd group |> List.ofSeq })
        |> List.ofSeq

    let initModel = { LastAccessGroups = None }

    let update msg model =
        match msg with
        | ExpandingCacheByLastAccess ->
            let model =
                if model.LastAccessGroups.IsSome then
                    model
                else
                    { model with
                        LastAccessGroups = loadByLastAccess () |> Some }

            model, Cmd.none

        | Remove group ->
            for file in group.Files do
                file.Delete()

            { model with
                LastAccessGroups = model.LastAccessGroups.Value |> List.except ([ group ]) |> Some },
            Cmd.none

    let private filterFilesByExtension extension group =
        group.Files |> List.filter (fun f -> f.Extension = extension)

    let private reportFiles label (files: FileInfo list) =
        let mbs = (files |> List.map _.Length |> List.sum |> float32) / 1024f / 1024f

        TextBlock($"{files.Length} {label} ({mbs:f2} Mb)")
            .horizontalAlignment(HorizontalAlignment.Right)
            .isVisible (files.Length > 0)

    let view model =
        Panel() {
            Expander(
                "Files accessed within the last...",
                ScrollViewer(
                    ItemsControl(
                        model.LastAccessGroups |> Option.defaultValue [],
                        fun group ->
                            (Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto; Star ]) {
                                TextBlock(group.Name).header ()
                                Button("🗑", Remove group).tooltip("clear this data").fontSize(20).gridRow (1)

                                (VStack(5) {
                                    let jsonFiles = group |> filterFilesByExtension JsonFileDataStore.FileExtension

                                    let videos =
                                        jsonFiles |> List.filter (fun f -> f.Name.StartsWith(Video.StorageKeyPrefix))

                                    let playlistsAndChannels = jsonFiles |> List.except videos

                                    group
                                    |> filterFilesByExtension VideoIndexRepository.FileExtension
                                    |> reportFiles "full-text indexes"

                                    reportFiles "playlist and channel caches" playlistsAndChannels
                                    reportFiles "video caches" videos
                                    group |> filterFilesByExtension "" |> reportFiles "thumbnails"
                                })
                                    .gridRowSpan(2)
                                    .gridColumn (1)
                            })
                                .margin (10)
                    )
                )
            )
                .onExpanding (fun _ -> ExpandingCacheByLastAccess)
        }
