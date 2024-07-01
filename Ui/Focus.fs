namespace Ui

open System.Runtime.CompilerServices
open Avalonia.Controls.Primitives
open Avalonia.Input
open Fabulous
open Fabulous.Avalonia

module FocusAttributes =
    let private focus (input: InputElement) =
        input.Focus(NavigationMethod.Unspecified) |> ignore

    /// Allows setting the Focus on an AutoCompleteBox.TemplateApplied
    let OnTemplateApplied =
        let rec focusOnce obj _ =
            let autoComplete = unbox<TemplatedControl> obj
            focus autoComplete
            autoComplete.TemplateApplied.RemoveHandler(focusOnce) // to clean up

        Attributes.defineBool "Focus_OnTemplateApplied" (fun _ newValueOpt node ->
            if newValueOpt.IsSome && newValueOpt.Value then
                let autoComplete = unbox<TemplatedControl> node.Target
                autoComplete.TemplateApplied.RemoveHandler(focusOnce) // to avoid duplicate handlers

                (*  Wait to call Focus() on AutoCompleteBox until after TemplateApplied
                    because of internal Avalonia AutoCompleteBox implementation:
                    FocusChanged only applies the Focus to the nested TextBox if it is set - which happens in OnApplyTemplate.
                    See https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Controls/AutoCompleteBox/AutoCompleteBox.cs *)
                autoComplete.TemplateApplied.AddHandler(focusOnce))

    /// Allows setting the Focus on an AutoCompleteBox.
    let Immediately =
        Attributes.defineBool "Focus_Immediately" (fun _ newValueOpt node ->
            if newValueOpt.IsSome && newValueOpt.Value then
                unbox<InputElement> node.Target |> focus)

type AutoCompleteBoxFocusModifiers =
    /// Sets the Focus on an IFabAutoCompleteBox if set is true; otherwise does nothing.
    [<Extension>]
    static member inline focus(this: WidgetBuilder<'msg, #IFabAutoCompleteBox>, set: bool) =
        this.AddScalar(FocusAttributes.OnTemplateApplied.WithValue(set))

type TextBoxFocusModifiers =
    /// Sets the Focus on an IFabTextBox if set is true; otherwise does nothing.
    [<Extension>]
    static member inline focus(this: WidgetBuilder<'msg, #IFabTextBox>, set: bool) =
        this.AddScalar(FocusAttributes.Immediately.WithValue(set))
