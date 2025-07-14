﻿namespace SubTubular.Gui

open System.Runtime.CompilerServices
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open Fabulous
open Fabulous.Avalonia
open type Fabulous.Avalonia.View

module Cursors =
    let hand = new Cursor(StandardCursorType.Hand)

module Icon =
    // prefixes
    let channel = "📺 "
    let playlist = "▶️ "
    let video = "📼 "
    let search = "🔍 "

    // notifications
    let error = "🛑 "
    let warning = "⚠️ "
    let info = "ⓘ "

    // unpadded
    let trash = "🗑"
    let help = "💡"
    let copy = "📋"
    let recent = "🕝"

    // suffixes
    let thumbnail = " 🖼"
    let videoCache = " 📦"
    let playlistLike = " 📜"
    let index = " 📚"
    let scopeSearch = " 🔎"

[<AutoOpen>]
module Styles =
    let avaloniaResourceUri fileName = "avares://SubTubular.Gui/" + fileName
    let appIconUrl = avaloniaResourceUri ("SubTubular.ico")

    let private getFactor (factor: float option) = factor |> Option.defaultValue 1

    // see https://docs.fabulous.dev/basics/user-interface/styling
    [<Extension>]
    type SharedStyle =
        [<Extension>]
        static member trailingMargin(this: WidgetBuilder<'msg, #IFabLayoutable>, ?bottomFactor: float) =
            this.margin (0, 0, 0, (getFactor bottomFactor) * float 5)

        [<Extension>]
        static member inline right(this: WidgetBuilder<'msg, #IFabLayoutable>) =
            this.horizontalAlignment (HorizontalAlignment.Right)

        [<Extension>]
        static member inline top(this: WidgetBuilder<'msg, #IFabLayoutable>) =
            this.verticalAlignment (VerticalAlignment.Top)

        [<Extension>]
        static member inline bottom(this: WidgetBuilder<'msg, #IFabLayoutable>) =
            this.verticalAlignment (VerticalAlignment.Bottom)

        [<Extension>]
        static member card(this: WidgetBuilder<'msg, #IFabControl>) = Border(this).classes ("card")

        [<Extension>]
        static member inline wrap(this: WidgetBuilder<'msg, #IFabTextBlock>) = this.textWrapping (TextWrapping.Wrap)

        [<Extension>]
        static member inline demoted(this: WidgetBuilder<'msg, IFabTextBlock>) = this.foreground (Colors.Gray)

        [<Extension>]
        static member inline header(this: WidgetBuilder<'msg, IFabTextBlock>) =
            this.fontSize(18).fontWeight (FontWeight.Bold)

        [<Extension>]
        static member inline smallDemoted(this: WidgetBuilder<'msg, IFabTextBlock>) = this.demoted().fontSize (11)

        [<Extension>]
        static member inline tooltip(this: WidgetBuilder<'msg, #IFabControl>, tooltip: string) =
            this.tip (ToolTip(tooltip))

        [<Extension>]
        static member inline tapCursor(this: WidgetBuilder<'msg, #IFabInputElement>) = this.cursor (Cursors.hand)

        [<Extension>]
        static member inline tappable
            (this: WidgetBuilder<'msg, #IFabControl>, msg: RoutedEventArgs -> 'msg, tooltip: string)
            =
            this.onTapped(msg).tapCursor().tooltip (tooltip)

        [<Extension>]
        static member inline tappable(this: WidgetBuilder<'msg, #IFabControl>, msg: 'msg, tooltip: string) =
            this.tappable ((fun _ -> msg), tooltip)

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
            this.styles ([ style ])

[<AutoOpen>]
module Widgets =
    let intUpDown min value max msg tooltip =
        NumericUpDown(min, max, Some value, msg)
            .formatString("F0") // to ditch the decimal digits
            .tooltip (tooltip + "\nenter a value, spin using mouse wheel, click or hold buttons")

    let uint16UpDown value msg tooltip =
        intUpDown (float 0) value (float System.UInt16.MaxValue) msg tooltip
