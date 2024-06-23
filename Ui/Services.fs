namespace Ui

open System.Text.Json
open SubTubular

[<AutoOpen>]
module Shared =
    let deepClone (obj: 'T) =
        let json = JsonSerializer.Serialize(obj)
        JsonSerializer.Deserialize<'T>(json)

module Services =
    let CacheFolder = Folder.GetPath Folders.cache
    let DataStore = JsonFileDataStore CacheFolder
    let Youtube = Youtube(DataStore, VideoIndexRepository CacheFolder)
