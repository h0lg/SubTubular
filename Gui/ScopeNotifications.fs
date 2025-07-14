namespace SubTubular.Gui

open Avalonia.Controls
open Avalonia.Media
open Fabulous.Avalonia
open SubTubular
open SubTubular.Extensions
open type Fabulous.Avalonia.View

module ScopeNotifications =
    type Model =
        { CaptionStatus: CommandScope.Notification array
          All: CommandScope.Notification list
          HasWarnings: bool
          HasError: bool }

    let initModel =
        { CaptionStatus = [||]
          All = []
          HasWarnings = false
          HasError = false }

    let update model (scope: CommandScope) (captionStates: CommandScope.Notification array option) =
        let captionStatus =
            if captionStates.IsSome then
                captionStates.Value
            else
                model.CaptionStatus

        let all = scope.Notifications |> Seq.append captionStatus |> List.ofSeq

        { model with
            CaptionStatus = captionStatus
            All = all
            HasError = all.HaveErrors()
            HasWarnings = all.HaveAnyOfLevel CommandScope.Notification.Levels.Warning }

    let private refreshCaptionTracksAfter =
        [| VideoList.Status.validated
           VideoList.Status.searched
           VideoList.Status.canceled |]

    let needsCaptionTracksUpdate state =
        refreshCaptionTracksAfter |> Array.contains state

    let updateCaptionTracks model (scope: CommandScope) =
        scope.GetCaptionTrackDownloadStates().Irregular().AsNotifications()
        |> Some
        |> update model scope

    let private flyout (notifications: CommandScope.Notification list) =
        let tb text = // TextAlignment override is required because centered text is inherited from host :(
            TextBlock(text).textAlignment (TextAlignment.Left)

        Flyout(
            ItemsControl(
                notifications,
                fun ntf ->
                    VStack() {
                        let icon =
                            match ntf.Level with
                            | CommandScope.Notification.Levels.Error -> Icon.error
                            | CommandScope.Notification.Levels.Warning -> Icon.warning
                            | CommandScope.Notification.Levels.Info -> Icon.info
                            | _ -> ""

                        tb (icon + ntf.Title)

                        if ntf.Video <> null then
                            tb ntf.Video.Title

                        if ntf.Message.IsNonEmpty() then
                            tb ntf.Message

                        if ntf.Errors <> null then
                            for err in ntf.Errors.GetRootCauses() do
                                tb err.Message
                    }
            )
        )
            .placement(PlacementMode.BottomEdgeAlignedRight)
            .showMode (FlyoutShowMode.Standard)

    let toggle model =
        let notifications = model.All

        let icon =
            if model.HasError then Icon.error
            else if model.HasWarnings then Icon.warning
            else Icon.info

        TextBlock($"{icon}\n{notifications.Length}")
            .centerText()
            .attachedFlyout(flyout notifications)
            .isVisible (not notifications.IsEmpty)
