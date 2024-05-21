namespace Ui

open System.Runtime.CompilerServices
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Media
open Avalonia.Styling
open Fabulous
open Fabulous.Avalonia
open type Fabulous.Avalonia.View

module Cursors =
    let hand = new Cursor(StandardCursorType.Hand)

[<AutoOpen>]
module Styles =
    let private getFactor (factor: float option) = factor |> Option.defaultValue 1

    // see https://docs.fabulous.dev/basics/user-interface/styling
    [<Extension>]
    type SharedStyle =
        [<Extension>]
        static member trailingMargin(this: WidgetBuilder<'msg, #IFabLayoutable>, ?bottomFactor: float) =
            this.margin (0, 0, 0, (getFactor bottomFactor) * float 5)

        [<Extension>]
        static member precedingSeparator(this: WidgetBuilder<'msg, #IFabLayoutable>, ?topFactor: float) =
            let factor = getFactor topFactor

            Border(this)
                .padding(0, factor * float 5, 0, 0)
                .borderThickness(0, 1, 0, 0)
                .borderBrush(Colors.Gray)
                .trailingMargin
                factor

        [<Extension>]
        static member inline separator(this: WidgetBuilder<'msg, #IFabLayoutable>) =
            Border(this).borderThickness(0, 0, 0, 1).borderBrush (Colors.Gray)

        [<Extension>]
        static member inline demoted(this: WidgetBuilder<'msg, IFabTextBlock>) = this.foreground (Colors.Gray)

        [<Extension>]
        static member inline smallDemoted(this: WidgetBuilder<'msg, IFabTextBlock>) = this.demoted().fontSize (11)

        [<Extension>]
        static member inline tooltip(this: WidgetBuilder<'msg, #IFabControl>, tooltip: string) =
            this.tip (ToolTip(tooltip))

        [<Extension>]
        static member inline tappable(this: WidgetBuilder<'msg, #IFabControl>, msg: 'msg, tooltip: string) =
            this.onTapped(fun _ -> msg).cursor(Cursors.hand).tooltip (tooltip)

        [<Extension>]
        static member inline asToggle(this: WidgetBuilder<'msg, #IFabTemplatedControl>, condition) =
            if condition then
                this.background (Colors.RoyalBlue)
            else
                this.background(Colors.Transparent).foreground (Colors.Gray)

        [<Extension>]
        static member inline acceptReturn(this: WidgetBuilder<'msg, #IFabAutoCompleteBox>) =
            // Create a style that targets TextBox within AutoCompleteBox
            let style =
                Style(_.OfType<AutoCompleteBox>().Template().OfType<TextBox>().Name("PART_TextBox"))

            style.Setters.Add(Setter(TextBox.AcceptsReturnProperty, box true))
            this.inlineStyles (style)
