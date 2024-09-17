using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring;

namespace SubTubular;

internal sealed class ResourceMonitor(IResourceMonitor resources)
{
    internal static readonly TimeSpan MaxCollectionWindow = TimeSpan.FromSeconds(5);
    private TimeSpan lastProcessorTime = GetTotalProcessorTime();
    private DateTime snapshotTaken = DateTime.UtcNow;
    private readonly object access = new(); // for thread-safe access

    private static TimeSpan GetTotalProcessorTime() => Process.GetCurrentProcess().TotalProcessorTime;

    internal bool HasSufficient()
    {
        var memoryPressure = GcMemoryPressure.GetLevel();
        double cpuUsage = GetCpuUsagePercentage(memoryPressure);
        return cpuUsage < 80 && memoryPressure < GcMemoryPressure.Level.High;
    }

    /// <summary>Calculates the average CPU usage percentage since the <see cref="lastProcessorTime"/> at <see cref="snapshotTaken"/> using the algorithm from
    /// https://github.com/AppMetrics/AppMetrics/blob/main/src/Extensions/src/App.Metrics.Extensions.Collectors/HostedServices/SystemUsageCollectorHostedService.cs
    /// and takes another snapshot.</summary>
    private double GetCpuUsagePercentage(GcMemoryPressure.Level memoryPressureForComparison)
    {
        lock (access)
        {
            var process = Process.GetCurrentProcess();
            var newProcTime = process.TotalProcessorTime;
            var totalCpuTimeUsed = newProcTime.TotalMilliseconds - lastProcessorTime.TotalMilliseconds;
            lastProcessorTime = newProcTime;
            TimeSpan elapsed = DateTime.UtcNow - snapshotTaken;
            /*TODO        see https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostic-resource-monitoring */
            var utilization = resources.GetUtilization(new[] { elapsed, MaxCollectionWindow }.Min());
            var system = utilization.SystemResources;
            var cpuTimeElapsed = elapsed.TotalMilliseconds * Environment.ProcessorCount;
            snapshotTaken = DateTime.UtcNow;
            double percentUsed = totalCpuTimeUsed * 100 / cpuTimeElapsed;
            Debug.WriteLine($"### resmon ######## CPU used custom/system {percentUsed}% | process {utilization.CpuUsedPercentage}%");
            Debug.WriteLine($"### resmon ######## RAM level custom/system {memoryPressureForComparison} | proc {utilization.MemoryUsedPercentage}% {utilization.MemoryUsedInBytes} Bytes | process.WorkingSet64 {process.WorkingSet64} bytes");
            return percentUsed;
        }
    }

    /// <summary>Helps determining the GC memory pressure.
    /// Borrowed from https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Utilities.cs
    /// until they make this API public. See also https://stackoverflow.com/a/750590 .</summary>
    private static class GcMemoryPressure
    {
        internal enum Level { Low, Medium, High }

        internal static Level GetLevel()
        {
            const double HighPressureThreshold = .90; // Percent of GC memory pressure threshold we consider "high"
            const double MediumPressureThreshold = .70; // Percent of GC memory pressure threshold we consider "medium"

            GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();
            if (memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * HighPressureThreshold) return Level.High;
            if (memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * MediumPressureThreshold) return Level.Medium;
            return Level.Low;
        }
    }
}

public static class Services
{
    public static IServiceCollection AddSubTubular(this IServiceCollection services)
        => services.AddLogging().AddResourceMonitoring(b =>
            b.ConfigureMonitor(o => o.CollectionWindow = ResourceMonitor.MaxCollectionWindow));
}