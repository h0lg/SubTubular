﻿namespace Ui

open System.IO
open System.Text.Json
open Avalonia.Styling
open Fabulous
open Fabulous.Avalonia
open SubTubular

open type Fabulous.Avalonia.View

//TODO also see instead https://docs.fabulous.dev/advanced/saving-and-restoring-app-state
module Settings =
    let private themeVariants =
        [ ThemeVariant.Dark; ThemeVariant.Default; ThemeVariant.Light ]

    type Model =
        { ThemeVariantKey: string
          ShowThumbnails: bool
          ResultOptions: ResultOptions.Model option
          FileOutput: FileOutput.Model option }

    type Msg =
        | ThemeVariantSelected of ThemeVariant option
        | ToggleThumbnails of bool
        | Save
        | Saved
        | Loaded of Model

    let getThemeVariant key =
        themeVariants |> List.find (fun tv -> tv.Key = key)

    let initModel =
        { ThemeVariantKey = ThemeVariant.Default.Key.ToString()
          ShowThumbnails = false
          ResultOptions = None
          FileOutput = None }

    let private path = Path.Combine(Folder.GetPath Folders.storage, "ui-settings.json")
    let requestSave = Cmd.debounce 1000 (fun () -> Save)

    let load =
        task {
            if File.Exists path then
                let! json = File.ReadAllTextAsync(path)
                let settings = JsonSerializer.Deserialize json
                return Loaded settings |> Some
            else
                return None
        }
        |> Cmd.OfTask.msgOption

    let save model =
        task {
            let json = JsonSerializer.Serialize model
            do! File.WriteAllTextAsync(path, json)
            return Saved
        }
        |> Cmd.OfTask.msg

    let update msg model =
        match msg with

        | ThemeVariantSelected v ->
            match v with
            | Some variant ->
                { model with
                    ThemeVariantKey = variant.Key.ToString() },
                requestSave ()
            | None -> model, Cmd.none

        | ToggleThumbnails on -> { model with ShowThumbnails = on }, requestSave ()
        | Save -> model, Cmd.none
        | Saved -> model, Cmd.none
        | Loaded model -> model, Cmd.none

    let private display (v: ThemeVariant) =
        if v = ThemeVariant.Light then
            "🌕 light"
        else if v = ThemeVariant.Dark then
            "🌑 dark"
        else if v = ThemeVariant.Default then
            "🌓 switch with system"
        else
            failwith $"unknown {nameof ThemeVariant} {v}"

    let view model =
        VStack(10.) {
            HWrap() {
                Label("Theme").centerVertical ()

                ListBox(themeVariants, (fun variant -> TextBlock(display variant)))
                    .selectedItem(getThemeVariant (model.ThemeVariantKey))
                    .onSelectionChanged(fun args ->
                        if args.AddedItems.Count > 0 then
                            let v = unbox<ThemeVariant> (args.AddedItems.Item 0)
                            ThemeVariantSelected(Some v)
                        else
                            ThemeVariantSelected None)
                    .itemsPanel (HWrapEmpty())
            }

            HStack() {
                Label("Download & display thumbnails").centerVertical ()
                ToggleSwitch(model.ShowThumbnails, ToggleThumbnails)
            }
        }
