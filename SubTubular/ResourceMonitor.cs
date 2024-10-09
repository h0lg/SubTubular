using System.Diagnostics;
using System.Runtime.CompilerServices;

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

internal sealed class ResourceAwareJobScheduler(TimeSpan delayBetweenHeatUps)
{
    private readonly ResourceMonitor resources = new();
    private uint running;

    internal async Task ParallelizeAsync(IEnumerable<Func<Task>> coldTasks, CancellationToken token)
    {
        SynchronizedCollection<Task> hotTasks = [];
        var cooking = HeatUp(coldTasks, hotTasks, token);

        while (!cooking.IsCompleted)
        {
            if (hotTasks.Count > 0)
            {
                var cooked = await Task.WhenAny(hotTasks); // wait for and get the first to complete
                Interlocked.Decrement(ref running);
                hotTasks.Remove(cooked);
            }

            // Prevent tight looping while waiting for new tasks
            if (hotTasks.Count == 0 && !cooking.IsCompleted) await Task.Delay(50, token);
        }

        await cooking; // let him cook to rethrow possible exns
    }

    internal async IAsyncEnumerable<T> ParallelizeAsync<T>(IEnumerable<Func<Task<T>>> coldTasks, [EnumeratorCancellation] CancellationToken token = default)
    {
        var hotTasks = new SynchronizedCollection<Task<T>>();
        var cooking = HeatUp(coldTasks, hotTasks, token);

        while (!cooking.IsCompleted)
        {
            if (hotTasks.Count > 0)
            {
                var cooked = await Task.WhenAny(hotTasks); // wait for and get the first to complete
                Interlocked.Decrement(ref running);
                hotTasks.Remove(cooked);
                yield return cooked.Result; // in order of completion
            }

            // Prevent tight looping while waiting for new tasks
            if (hotTasks.Count == 0 && !cooking.IsCompleted) await Task.Delay(50, token);
        }

        await cooking; // let him cook to rethrow possible exns
    }

    /// <summary>Starts the <paramref name="coldTasks"/> one after the other
    /// respecting the available <see cref="resources"/> and the <see cref="delayBetweenHeatUps"/>
    /// adding them to the running <paramref name="hotTasks"/> until the <paramref name="token"/> is cancelled.</summary>
    private async Task HeatUp<T>(IEnumerable<Func<T>> coldTasks, SynchronizedCollection<T> hotTasks, CancellationToken token) where T : Task
    {
        var orders = new Queue<Func<T>>(coldTasks);

        while (!token.IsCancellationRequested && orders.Count > 0)
        {
            // start a cold task if none is running or we have sufficient resources
            if (running == 0 || resources.HasSufficient())
            {
                var heatUp = orders.Dequeue();
                hotTasks.Add(heatUp()); // starts the task
                Interlocked.Increment(ref running);
            }

            if (!token.IsCancellationRequested) await Task.Delay(delayBetweenHeatUps, token);
        }
    }
}
