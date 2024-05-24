namespace Ui

open SubTubular

module Services =
    let private cacheFolder = Folder.GetPath Folders.cache
    let DataStore = JsonFileDataStore cacheFolder
    let Youtube = Youtube(DataStore, VideoIndexRepository cacheFolder)
