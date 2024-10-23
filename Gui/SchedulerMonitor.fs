namespace SubTubular.Gui

open Fabulous.Avalonia
open SubTubular
open type Fabulous.Avalonia.View

module SchedulerMonitor =
    type Msg =
        | Updated
        | QueueProgressChanged of float
        | CpuUsageChanged of float
        | GcMemoryPressureChanged of float

    let render (model: JobSchedulerReporter) =
        (VStack(1) {
            ProgressBar(0, 100, float model.CpuUsage, CpuUsageChanged)
                // see https://docs.avaloniaui.net/docs/reference/controls/progressbar#progresstextformat-example
                .progressTextFormat("CPU usage : {1:0}%")
                .fontSize(8)
                .showProgressText (true)

            ProgressBar(0, 2, float model.GcMemoryPressure, GcMemoryPressureChanged)
                .progressTextFormat($"GC memory pressure : {model.GcMemoryPressure}")
                .fontSize(8)
                .showProgressText (true)

            ProgressBar(0, float model.All, float model.Completed, QueueProgressChanged)
                .progressTextFormat($"{model.Queued} queued {model.Running} running {model.Completed} completed")
                .fontSize(8)
                .showProgressText (true)
        })
            .isVisible(model.Queues > 0u)
            .onJobSchedulerReporterUpdated (model, Updated)
