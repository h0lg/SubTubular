namespace Ui

open System.Runtime.CompilerServices
open Avalonia.Input
open Fabulous
open Fabulous.Avalonia

module FocusAttributes =
    /// Allows setting the Focus on an Avalonia.Input.InputElement
    let Focus =
        Attributes.defineBool "Focus" (fun oldValueOpt newValueOpt node ->
            let target = node.Target :?> InputElement

            let rec onAttached obj args =
                target.Focus() |> ignore
                target.AttachedToVisualTree.RemoveHandler(onAttached) // to clean up

            if newValueOpt.IsSome && newValueOpt.Value then
                target.AttachedToVisualTree.AddHandler(onAttached))

type FocusModifiers =
    [<Extension>]
    /// Sets the Focus on an IFabInputElement if set is true; otherwise does nothing.
    static member inline focus(this: WidgetBuilder<'msg, #IFabInputElement>, set: bool) =
        this.AddScalar(FocusAttributes.Focus.WithValue(set))
