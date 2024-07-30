namespace Ui

open System.Runtime.CompilerServices
open Avalonia.Controls
open Avalonia.Input
open Fabulous
open Fabulous.Avalonia

module FocusAttributes =
    /// Allows setting the Focus on an AutoCompleteBox
    let Focus =
        let rec focusOnce obj _ =
            let autoComplete = unbox<AutoCompleteBox> obj
            autoComplete.Focus(NavigationMethod.Unspecified) |> ignore
            autoComplete.TemplateApplied.RemoveHandler(focusOnce) // to clean up

        Attributes.defineBool "Focus" (fun _ newValueOpt node ->
            if newValueOpt.IsSome && newValueOpt.Value then
                let autoComplete = unbox<AutoCompleteBox> node.Target
                autoComplete.TemplateApplied.RemoveHandler(focusOnce) // to avoid duplicate handlers

                (*  Wait to call Focus() on AutoCompleteBox until after TemplateApplied
                    because of internal Avalonia AutoCompleteBox implementation:
                    FocusChanged only applies the Focus to the nested TextBox if it is set - which happens in OnApplyTemplate.
                    See https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Controls/AutoCompleteBox/AutoCompleteBox.cs *)
                autoComplete.TemplateApplied.AddHandler(focusOnce))

type FocusModifiers =
    /// Sets the Focus on an IFabAutoCompleteBox if set is true; otherwise does nothing.
    [<Extension>]
    static member inline focus(this: WidgetBuilder<'msg, #IFabAutoCompleteBox>, set: bool) =
        this.AddScalar(FocusAttributes.Focus.WithValue(set))
