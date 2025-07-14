﻿namespace SubTubular.Gui

open System.Runtime.CompilerServices
open Avalonia
open Avalonia.Styling
open Fabulous
open Fabulous.Avalonia
open SubTubular

module StyledElement =
    /// Allows setting the DataContextProperty on a StyledElement
    let DataContext =
        Attributes.defineAvaloniaPropertyWithEquality StyledElement.DataContextProperty

    /// Allows adding inline styles to a StyledElement.
    let InlineStyles =
        Attributes.defineProperty "StyledElement_InlineStyles" Unchecked.defaultof<IStyle seq> (fun target values ->
            (target :?> StyledElement).Styles.AddRange values)

module CommandScopeAttributes =
    /// Allows attaching a handler to the ProgressChanged event of a CommandScope to a StyledElement
    let ProgressChanged =
        Attributes.defineEventNoArg "CommandScope_ProgressChanged" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<CommandScope> element.DataContext
            model.ProgressChanged)

    /// Allows attaching a handler to the Notified event of a CommandScope to a StyledElement
    let Notified =
        Attributes.defineEvent "CommandScope_Notified" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<CommandScope> element.DataContext
            model.Notified)

type StyledElementModifiers =
    /// Dispatches a msg on scope.ProgressChanged.
    [<Extension>]
    static member inline onScopeProgressChanged
        (this: WidgetBuilder<'msg, #IFabStyledElement>, scope: CommandScope, msg: 'msg)
        =
        this
            // set DataContext for it to be available in CommandScopeAttributes.ProgressChanged
            .AddScalar(StyledElement.DataContext.WithValue(scope))
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

    /// <summary>Adds inline styles used by the widget and its descendants.</summary>
    /// <param name="this">Current widget.</param>
    /// <param name="value">Inline styles to be used for the widget and its descendants.</param>
    /// <remarks>Note: Fabulous will recreate the Style/Styles during the view diffing as opposed to a single styled element property.</remarks>
    [<Extension>]
    static member inline styles(this: WidgetBuilder<'msg, #IFabStyledElement>, value: IStyle list) =
        this.AddScalar(StyledElement.InlineStyles.WithValue(value))

    /// <summary>Add inline style used by the widget and its descendants.</summary>
    /// <param name="this">Current widget.</param>
    /// <param name="value">Inline style to be used for the widget and its descendants.</param>
    /// <remarks>Note: Fabulous will recreate the Style/Styles during the view diffing as opposed to a single styled element property.</remarks>
    [<Extension>]
    static member inline styles(this: WidgetBuilder<'msg, #IFabStyledElement>, value: IStyle) =
        StyledElementModifiers.styles (this, [ value ])
