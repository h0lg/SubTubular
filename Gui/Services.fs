namespace SubTubular.Gui

open System
open System.Text.Json
open Avalonia.Controls.Notifications
open Avalonia.Interactivity
open Fabulous
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Diagnostics.ResourceMonitoring
open SubTubular

[<AutoOpen>]
module Shared =
    type CommonMsg =
        | ToggleFlyout of RoutedEventArgs
        | OpenUrl of string
        | Notify of string
        | NotifyLong of string * string
        | Fail of string
        | FailLong of string * string

    let deepClone (obj: 'T) =
        let json = JsonSerializer.Serialize(obj)
        JsonSerializer.Deserialize<'T>(json)

module Services =
    let CacheFolder = Folder.GetPath Folders.cache
    let DataStore = JsonFileDataStore CacheFolder

    let Youtube =
        let services = ServiceCollection().AddSubTubular()
        let serviceProvider = services.BuildServiceProvider()
        let resources = serviceProvider.GetService<IResourceMonitor>()
        Youtube(DataStore, VideoIndexRepository CacheFolder, resources)

module Notify =
    let mutable via: WindowNotificationManager = null
    let nonExpiring = TimeSpan.Zero // see https://reference.avaloniaui.net/api/Avalonia.Controls.Notifications/Notification/

    let private notify notification =
        Dispatch.toUiThread (fun () -> via.Show(notification))

    let info title message (expiration: TimeSpan) =
        notify (Notification(title, message, NotificationType.Information, expiration))
        Cmd.none

    let error title message =
        notify (Notification(title, message, NotificationType.Error, nonExpiring))
        Cmd.none
