namespace Ui

open System
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

    /// <summary>Adds inline styles used by the widget and its descendants.</summary>
    /// <param name="this">Current widget.</param>
    /// <param name="styles">Inline styles to be used for the widget and its descendants.</param>
    [<Extension>]
    static member inline inlineStyles(this: WidgetBuilder<'msg, #IFabStyledElement>, [<ParamArray>] styles: IStyle[]) =
        this.AddScalar(StyledElement.InlineStyles.WithValue(styles))
