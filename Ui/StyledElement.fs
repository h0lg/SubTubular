namespace Ui

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
