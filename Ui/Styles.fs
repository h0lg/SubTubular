namespace Ui

open System.Runtime.CompilerServices
open Avalonia.Media
open Fabulous
open Fabulous.Avalonia

module Styles =
    // see https://docs.fabulous.dev/basics/user-interface/styling
    [<Extension>]
    type SharedStyle =

        [<Extension>]
        static member inline trailingMargin(this: WidgetBuilder<'msg, #IFabLayoutable>) = this.margin (0, 0, 0, 5)

        [<Extension>]
        static member inline demoted(this: WidgetBuilder<'msg, IFabTextBlock>) = this.foreground (Colors.Gray)
