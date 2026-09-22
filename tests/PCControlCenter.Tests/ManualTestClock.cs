// Deterministic monotonic clock: timer tests never write hardware or wait minutes.
sealed class ManualTestClock : TimeProvider
{
    private long ticks;
    private readonly List<TestTimer> timers = new();
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(this, callback, state);
        timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }
    public void Advance(int seconds)
    {
        ticks += TimeSpan.FromSeconds(seconds).Ticks;
        foreach (var timer in timers.ToArray())
            timer.Fire();
    }
    private sealed class TestTimer(ManualTestClock clock, TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;
        private long due = long.MaxValue;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed)
                return false;
            due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks;
            return true;
        }
        public void Fire()
        {
            if (disposed || due > clock.ticks)
                return;
            due = long.MaxValue;
            callback(state);
        }
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
