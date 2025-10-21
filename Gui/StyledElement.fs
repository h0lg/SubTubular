namespace SubTubular.Gui

open System.Runtime.CompilerServices
open Avalonia
open Avalonia.Layout
open Fabulous
open Fabulous.Avalonia
open SubTubular

module StyledElement =
    /// Allows setting the DataContextProperty on a StyledElement
    let DataContext =
        Attributes.defineAvaloniaPropertyWithEquality StyledElement.DataContextProperty

module ThrottledEventAttributes =
    /// Allows attaching a handler to the Event of a ThrottledEvent to a StyledElement
    let Triggered =
        Attributes.Mvu.defineEventNoArg "ThrottledEvent_Event" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<ThrottledEvent> element.DataContext
            model.Triggered)

module CommandScopeAttributes =
    /// Allows attaching a handler to the Notified event of a CommandScope to a StyledElement
    let Notified =
        Attributes.Mvu.defineEvent "CommandScope_Notified" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<CommandScope> element.DataContext
            model.Notified)

type StyledElementModifiers =
    /// Dispatches a throttled msg on throttledEvent.Triggered.
    [<Extension>]
    static member inline onThrottledEvent
        (this: WidgetBuilder<'msg, #IFabStyledElement>, throttledEvent: ThrottledEvent, msg: 'msg)
        =
        this
            // set DataContext for it to be available in ThrottledEventAttributes.Triggered
            .AddScalar(StyledElement.DataContext.WithValue(throttledEvent))
            .AddScalar(ThrottledEventAttributes.Triggered.WithValue(MsgValue msg))

    /// Dispatches a msg on scope.Notified.
    [<Extension>]
    static member inline onScopeNotified
        (this: WidgetBuilder<'msg, #IFabStyledElement>, scope: CommandScope, msg: CommandScope.Notification -> 'msg)
        =
        this
            // set DataContext for it to be available in CommandScopeAttributes.Notified
            .AddScalar(StyledElement.DataContext.WithValue(scope))
            .AddScalar(CommandScopeAttributes.Notified.WithValue(msg))

type LayoutableModifiers =
    /// <summary>Link a ViewRef to access the direct Layoutable control instance.</summary>
    /// <param name="this">Current widget.</param>
    /// <param name="value">The ViewRef instance that will receive access to the underlying control.</param>
    [<Extension>]
    static member inline reference(this: WidgetBuilder<'msg, #IFabLayoutable>, value: ViewRef<Layoutable>) =
        this.AddScalar(ViewRefAttributes.ViewRef.WithValue(value.Unbox))
