namespace Ui

open System.Text.Json
open SubTubular

[<AutoOpen>]
module Shared =
    let deepClone (obj: 'T) =
        let json = JsonSerializer.Serialize(obj)
        JsonSerializer.Deserialize<'T>(json)

module Services =
    let private cacheFolder = Folder.GetPath Folders.cache
    let DataStore = JsonFileDataStore cacheFolder
    let Youtube = Youtube(DataStore, VideoIndexRepository cacheFolder)
