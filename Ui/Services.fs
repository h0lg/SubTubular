namespace Ui

open System
open System.Text.Json
open Avalonia.Controls.Notifications
open Avalonia.Interactivity
open SubTubular
open Fabulous

[<AutoOpen>]
module Shared =
    type CommonMsg =
        | ToggleFlyout of RoutedEventArgs
        | OpenUrl of string
        | Notify of string
        | Fail of string

    let deepClone (obj: 'T) =
        let json = JsonSerializer.Serialize(obj)
        JsonSerializer.Deserialize<'T>(json)

module Services =
    let CacheFolder = Folder.GetPath Folders.cache
    let DataStore = JsonFileDataStore CacheFolder
    let Youtube = Youtube(DataStore, VideoIndexRepository CacheFolder)
    let mutable Notifier: WindowNotificationManager = null

    let private notify notification =
        Dispatch.toUiThread (fun () -> Notifier.Show(notification))

    let notifyInfo title =
        notify (Notification(title, "", NotificationType.Information, TimeSpan.FromSeconds 3))
        Cmd.none

    let notifyError title message =
        notify (Notification(title, message, NotificationType.Error))
        Cmd.none
