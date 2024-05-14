namespace Ui

open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Fabulous
open Fabulous.Avalonia

module WidgetHelpers =
    /// Compiles the templateBuilder into a template.
    let compileTemplate (templateBuilder: 'item -> WidgetBuilder<'msg, 'widget>) item =
        let itm = unbox<'item> item
        (templateBuilder itm).Compile()

module AutoCompleteBox =

    let Text =
        Attributes.defineAvaloniaPropertyWithChangedEvent' "AutoCompleteBox_TextChanged" AutoCompleteBox.TextProperty

    /// Allows setting the ItemTemplate on an AutoCompleteBox
    let ItemTemplate =
        Attributes.defineSimpleScalar<obj -> Widget>
            "AutoCompleteBox_ItemTemplate"
            (fun a b ->
                if LanguagePrimitives.PhysicalEquality a b then
                    ScalarAttributeComparison.Identical
                else
                    ScalarAttributeComparison.Different)
            (fun _ newValueOpt node ->
                let autoComplete = node.Target :?> AutoCompleteBox

                match newValueOpt with
                | ValueNone -> autoComplete.ClearValue(AutoCompleteBox.ItemTemplateProperty)
                | ValueSome template ->
                    autoComplete.SetValue(AutoCompleteBox.ItemTemplateProperty, WidgetDataTemplate(node, template))
                    |> ignore)

[<AutoOpen>]
module AutoCompleteBoxBuilders =
    type Fabulous.Avalonia.View with

        /// <summary>Creates an AutoCompleteBox widget.</summary>
        /// <param name="items">The items to display.</param>
        static member inline AutoCompleteBox(items: seq<_>) =
            WidgetBuilder<'msg, IFabAutoCompleteBox>(
                AutoCompleteBox.WidgetKey,
                AutoCompleteBox.ItemsSource.WithValue(items)
            )

        /// <summary>Creates an AutoCompleteBox widget.</summary>
        /// <param name="populator">The function to populate the items.</param>
        static member inline AutoCompleteBox(populator: string -> CancellationToken -> Task<seq<_>>) =
            WidgetBuilder<'msg, IFabAutoCompleteBox>(
                AutoCompleteBox.WidgetKey,
                AutoCompleteBox.AsyncPopulator.WithValue(populator)
            )

type AutoCompleteBoxModifiers =

    /// <summary>Binds the AutoCompleteBox.TextProperty.</summary>
    /// <param name="this">Current widget.</param>
    /// <param name="text">The value to bind.</param>
    /// <param name="fn">A function mapping the updated text to a 'msg to raise on user change.</param>
    [<Extension>]
    static member inline onTextChanged
        (this: WidgetBuilder<'msg, #IFabAutoCompleteBox>, text: string, fn: string -> 'msg)
        =
        this.AddScalar(AutoCompleteBox.Text.WithValue(ValueEventData.create text fn))

    /// <summary>Sets the ItemTemplate property.</summary>
    /// <param name="this">Current widget.</param>
    /// <param name="template">The template to render the items with.</param>
    [<Extension>]
    static member inline itemTemplate
        (this: WidgetBuilder<'msg, #IFabAutoCompleteBox>, template: 'item -> WidgetBuilder<'msg, 'widget>)
        =
        this.AddScalar(AutoCompleteBox.ItemTemplate.WithValue(WidgetHelpers.compileTemplate template))
