namespace Ui

open System
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Fabulous.Avalonia
open type Fabulous.Avalonia.View
open SubTubular
open SubTubular.Extensions
open Styles

module SearchResult =
    type Msg =
        | CopyingToClipboard of RoutedEventArgs
        | OpenUrl of string

    // see https://github.com/AvaloniaUI/Avalonia/discussions/9654
    let private writeHighlightingMatches (matched: MatchedText) (matchPadding: uint32 option) =
        let tb = SelectableTextBlock(CopyingToClipboard)

        let padding =
            match matchPadding with
            | Some value -> Nullable(value)
            | None -> Nullable()

        let runs =
            matched.WriteHighlightingMatches(
                (fun text -> Run(text)),
                (fun text -> Run(text).foreground (Colors.Orange)),
                padding
            )

        let contents = runs |> Seq.map tb.Yield |> Seq.toList

        let content =
            Seq.fold (fun agg cont -> tb.Combine(agg, cont)) contents.Head contents.Tail

        (tb.Run content).textWrapping (TextWrapping.WrapWithOverflow)

    let render (matchPadding: uint32) (result: VideoSearchResult) =
        let videoUrl = Youtube.GetVideoUrl result.Video.Id

        (VStack() {
            Grid(coldefs = [ Auto; Auto; Star ], rowdefs = [ Auto ]) {
                (match result.TitleMatches with
                 | null -> SelectableTextBlock(result.Video.Title, CopyingToClipboard)
                 | matches -> writeHighlightingMatches matches None)
                    .fontSize (18)

                Button("↗", OpenUrl videoUrl)
                    .tip(ToolTip("Open video in browser"))
                    .padding(5, 1)
                    .margin(5, 0)
                    .gridColumn (1)

                TextBlock("📅" + result.Video.Uploaded.ToString())
                    .tip(ToolTip("uploaded"))
                    .textAlignment(TextAlignment.Right)
                    .gridColumn (2)
            }

            if result.DescriptionMatches <> null then
                HStack() {
                    (TextBlock "in description").demoted ()

                    for matches in result.DescriptionMatches.SplitIntoPaddedGroups(matchPadding) do
                        writeHighlightingMatches matches (Some matchPadding)
                }

            if result.KeywordMatches.HasAny() then
                HStack() {
                    (TextBlock "in keywords").demoted ()

                    for matches in result.KeywordMatches do
                        writeHighlightingMatches matches (Some matchPadding)
                }

            if result.MatchingCaptionTracks.HasAny() then
                for trackResult in result.MatchingCaptionTracks do
                    TextBlock(trackResult.Track.LanguageName).demoted ()

                    let displaysHour = trackResult.HasMatchesWithHours(matchPadding)
                    let splitMatches = trackResult.Matches.SplitIntoPaddedGroups(matchPadding)

                    for matched in splitMatches do
                        let (synced, captionAt) =
                            trackResult.SyncWithCaptions(matched, matchPadding).ToTuple()

                        let offset =
                            TimeSpan
                                .FromSeconds(captionAt)
                                .FormatWithOptionalHours()
                                .PadLeft(if displaysHour then 7 else 5)

                        Grid(coldefs = [ Auto; Auto; Star ], rowdefs = [ Auto ]) {
                            (TextBlock offset).demoted ()

                            Button("↗", OpenUrl $"{videoUrl}?t={captionAt}")
                                .tip(ToolTip($"Open video at {offset} in browser"))
                                .padding(5, 1)
                                .margin(5, 0)
                                .verticalAlignment(VerticalAlignment.Top)
                                .gridColumn (1)

                            (writeHighlightingMatches synced (Some matchPadding)).gridColumn (2)
                        }
        })
