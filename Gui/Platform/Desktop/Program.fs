namespace SubTubular.Gui.Desktop

open System
open Avalonia
open SubTubular.Gui

module Program =

    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp () = App.create().UsePlatformDetect()

    [<EntryPoint; STAThread>]
    let main argv =
        buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)
