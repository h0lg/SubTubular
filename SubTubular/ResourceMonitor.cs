using System.Diagnostics;

namespace SubTubular;

internal sealed class ResourceMonitor
{
    private TimeSpan lastProcessorTime = GetTotalProcessorTime();
    private DateTime snapshotTaken = DateTime.UtcNow;
    private readonly object access = new(); // for thread-safe access

    private static TimeSpan GetTotalProcessorTime() => Process.GetCurrentProcess().TotalProcessorTime;

    internal bool HasSufficient()
    {
        double cpuUsage = GetCpuUsagePercentage();
        var memoryPressure = GcMemoryPressure.GetLevel();
        return cpuUsage < 80 && memoryPressure < GcMemoryPressure.Level.High;
    }

    /// <summary>Calculates the average CPU usage percentage since the <see cref="lastProcessorTime"/> at <see cref="snapshotTaken"/> using the algorithm from
    /// https://github.com/AppMetrics/AppMetrics/blob/main/src/Extensions/src/App.Metrics.Extensions.Collectors/HostedServices/SystemUsageCollectorHostedService.cs
    /// and takes another snapshot.</summary>
    private double GetCpuUsagePercentage()
    {
        lock (access)
        {
            var newProcTime = GetTotalProcessorTime();
            var totalCpuTimeUsed = newProcTime.TotalMilliseconds - lastProcessorTime.TotalMilliseconds;
            lastProcessorTime = newProcTime;
            var cpuTimeElapsed = (DateTime.UtcNow - snapshotTaken).TotalMilliseconds * Environment.ProcessorCount;
            snapshotTaken = DateTime.UtcNow;
            return totalCpuTimeUsed * 100 / cpuTimeElapsed;
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
