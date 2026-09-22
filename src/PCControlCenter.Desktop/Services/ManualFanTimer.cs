using System;
using System.Threading;

namespace PCControlCenter.Desktop.Services;

// Runs independently of the UI and telemetry, including while minimized.
public sealed class ManualFanTimer(TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object gate = new();
    private ITimer? timer;
    private long generation;
    public long? Deadline
    {
        get; private set;
    }
    public TimeSpan? Remaining => Deadline is long end
        ? TimeSpan.FromSeconds(Math.Max(0, (end - clock.GetTimestamp()) / (double)clock.TimestampFrequency)) : null;
    public bool IsExpired(long? deadline) => deadline is long end && clock.GetTimestamp() >= end;
    public void Start(CancellationTokenSource session, TimeSpan? duration) =>
        Resume(session, duration.HasValue ? clock.GetTimestamp() + (long)(duration.Value.TotalSeconds * clock.TimestampFrequency) : null);
    public void Resume(CancellationTokenSource session, long? deadline)
    {
        lock (gate)
        {
            Clear();
            Deadline = deadline;
            if (!deadline.HasValue)
                return;
            long current = generation;
            timer = clock.CreateTimer(_ =>
            {
                lock (gate)
                {
                    if (current != generation)
                        return;
                    try
                    {
                        session.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                }
            }, null, Remaining!.Value, Timeout.InfiniteTimeSpan);
        }
    }
    public void Clear()
    {
        lock (gate)
        {
            generation++;
            timer?.Dispose();
            timer = null;
            Deadline = null;
        }
    }
    public void Dispose() => Clear();
}
