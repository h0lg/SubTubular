namespace SubTubular;

/// <summary><para>A throttled event dispatcher that raises events at most once per <paramref name="interval"/>
/// in the given <paramref name="syncContext"/> or the <see cref="SynchronizationContext.Current"/> (e.g. UI thread dispatcher).
/// This ensures the event handlers are raised on the same thread that created the dispatcher.</para>
/// <para>Events can be forwarded via <see cref="Invoke(object, EventArgs)"/> and the <see cref="latestSender"/> and <see cref="latestArgs"/>
/// are dispatched via <see cref="Event"/>.</para></summary>
public class ThrottledEvent(TimeSpan interval, SynchronizationContext? syncContext = null)
{
    private readonly SynchronizationContext syncContext = syncContext ?? SynchronizationContext.Current
        ?? throw new InvalidOperationException("No SynchronizationContext available. Use this from a UI thread or provide one explicitly.");

    private readonly object locker = new();

    private object? latestSender;
    private EventArgs? latestArgs;
    private bool hasPending;
    private bool isRunning;

    /// <summary>Raised at most once per interval with the latest sender/args pair.</summary>
    public event EventHandler? Event;

    /// <summary>Push a non-generic event into the throttler.
    /// Only the latest sender/args pair is retained during the interval.</summary>
    public void Invoke(object sender, EventArgs args)
    {
        lock (locker)
        {
            latestSender = sender;
            latestArgs = args;
            hasPending = true;

            if (!isRunning)
            {
                isRunning = true;
                Task.Run(DispatchDelayed);
            }
        }
    }

    private async Task DispatchDelayed()
    {
        while (true)
        {
            await Task.Delay(interval);
            (object sender, EventArgs args)? next = null;

            lock (locker)
            {
                if (hasPending)
                {
                    next = (latestSender!, latestArgs!);
                    hasPending = false;
                }
                else
                {
                    isRunning = false;
                    return;
                }
            }

            // Raise events on the original thread (usually the UI thread)
            if (next is var (sender, args) && args != null)
                syncContext.Post(_ => Event?.Invoke(sender, args), null);
        }
    }
}
