namespace SubTubular.Gui

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
        Attributes.Mvu.defineEventNoArg "CommandScope_ProgressChanged" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<CommandScope> element.DataContext
            model.ProgressChanged)

    /// Allows attaching a handler to the Notified event of a CommandScope to a StyledElement
    let Notified =
        Attributes.Mvu.defineEvent "CommandScope_Notified" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<CommandScope> element.DataContext
            model.Notified)

module JobSchedulerReporterAttributes =
    /// Allows attaching a handler to the Updated event of a JobSchedulerReporter to a StyledElement
    let Updated =
        Attributes.Mvu.defineEventNoArg "JobSchedulerReporter_Updated" (fun target ->
            let element = unbox<StyledElement> target
            let model = unbox<JobSchedulerReporter> element.DataContext
            model.Updated)

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

    /// Dispatches a msg on scope.Notified.
    [<Extension>]
    static member inline onScopeNotified
        (this: WidgetBuilder<'msg, #IFabStyledElement>, scope: CommandScope, msg: CommandScope.Notification -> 'msg) =
        this
            // set DataContext for it to be available in CommandScopeAttributes.Notified
            .AddScalar(StyledElement.DataContext.WithValue(scope))
            .AddScalar(CommandScopeAttributes.Notified.WithValue(msg))

    /// Dispatches a msg on scheduler.Updated.
    [<Extension>]
    static member inline onJobSchedulerReporterUpdated
        (this: WidgetBuilder<'msg, #IFabStyledElement>, scheduler: JobSchedulerReporter, msg: 'msg)
        =
        this
            // set DataContext for it to be available in CommandScopeAttributes.Notified
            .AddScalar(StyledElement.DataContext.WithValue(scheduler))
            .AddScalar(JobSchedulerReporterAttributes.Updated.WithValue(MsgValue msg))
