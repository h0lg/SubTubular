namespace Ui

open System.Runtime.CompilerServices
open Avalonia.Styling
open Fabulous
open Fabulous.Avalonia

module ApplicationEx =
    let RequestedThemeVariant =
        Attributes.definePropertyWithGetSet
            "Application_RequestedThemeVariant"
            (fun _ -> FabApplication.Current.ActualThemeVariant)
            (fun _ value -> FabApplication.Current.RequestedThemeVariant <- value)

type ApplicationModifiersEx =
    [<Extension>]
    static member requestedThemeVariant(this: WidgetBuilder<'msg, #IFabApplication>, value: ThemeVariant) =
        this.AddScalar(ApplicationEx.RequestedThemeVariant.WithValue(value))
