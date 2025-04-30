namespace SubTubular.Gui

open System
open System.Runtime.CompilerServices
open Avalonia
open Fabulous
open Fabulous.Avalonia
open SubTubular

module StyledElement =
    /// Allows setting the DataContextProperty on a StyledElement
    let DataContext =
        Attributes.defineAvaloniaPropertyWithEquality StyledElement.DataContextProperty

module CommandScopeAttributes =
    /// Allows attaching a handler to the ProgressChanged event of a CommandScope to a StyledElement
    let ProgressChanged =
        Attributes.Mvu.defineEventNoArg "CommandScope_ProgressChanged" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<ThrottledEvent> element.DataContext
            model.Event)

    /// Allows attaching a handler to the Notified event of a CommandScope to a StyledElement
    let Notified =
        Attributes.Mvu.defineEvent "CommandScope_Notified" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<CommandScope> element.DataContext
            model.Notified)

type StyledElementModifiers =
    /// Dispatches a throttled msg on scope.ProgressChanged.
    [<Extension>]
    static member inline onScopeProgressChanged
        (this: WidgetBuilder<'msg, #IFabStyledElement>, scope: CommandScope, msInterval: float, msg: 'msg)
        =
        let throttledEvent = ThrottledEvent(TimeSpan.FromMilliseconds(msInterval))
        scope.ProgressChanged.Add(fun args -> throttledEvent.Invoke(scope, args))

        this
            // set DataContext for it to be available in CommandScopeAttributes.ProgressChanged
            .AddScalar(StyledElement.DataContext.WithValue(throttledEvent))
            .AddScalar(CommandScopeAttributes.ProgressChanged.WithValue(MsgValue msg))

    /// Dispatches a msg on scope.Notified.
    [<Extension>]
    static member inline onScopeNotified
        (this: WidgetBuilder<'msg, #IFabStyledElement>, scope: CommandScope, msg: CommandScope.Notification -> 'msg)
        =
        this
            // set DataContext for it to be available in CommandScopeAttributes.Notified
            .AddScalar(StyledElement.DataContext.WithValue(scope))
            .AddScalar(CommandScopeAttributes.Notified.WithValue(msg))
