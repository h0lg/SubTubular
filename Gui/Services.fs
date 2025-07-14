﻿namespace SubTubular.Gui

open System
open System.Text.Json
open Avalonia.Controls.Notifications
open SubTubular
open Fabulous

[<AutoOpen>]
module Shared =
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
