namespace SubTubular.Gui.Desktop

open System
open Avalonia
open SubTubular.Gui

module Program =

    let private logError (error: string) =
#if DEBUG
        Console.Error.WriteLine(error)
#else
        SubTubular.ErrorLog.Write(error) |> ignore
#endif

    let private setupGlobalExceptionHandlers () =
        AppDomain.CurrentDomain.UnhandledException.Add(fun args ->
            let ex = args.ExceptionObject :?> Exception
            logError $"[AppDomain] Unhandled exception: {ex}")

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException.Add(fun args ->
            logError $"[TaskScheduler] Unobserved task exception: {args.Exception}"
            args.SetObserved() // prevents the process from crashing
        )

    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp () = App.create().UsePlatformDetect()

    [<EntryPoint; STAThread>]
    let main argv =
        setupGlobalExceptionHandlers ()
        buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)
