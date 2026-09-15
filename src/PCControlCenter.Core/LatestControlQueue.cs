namespace PCControlCenter.Core;

/// <summary>Coalesces pending UI values without interrupting an active write.</summary>
public sealed class LatestControlQueue : IAsyncDisposable
{
    private sealed record Pending(ControlRequest Request, CancellationToken Cancellation, TaskCompletionSource<ControlResult> Completion);
    private readonly Controller controller;
    private readonly Feature feature;
    private readonly TimeSpan window;
    private readonly object gate = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly Task worker;
    private Pending? pending;
    private bool closing;

    public LatestControlQueue(Controller controller, Feature feature, TimeSpan? window = null)
    {
        this.controller = controller;
        this.feature = feature;
        this.window = window ?? TimeSpan.FromMilliseconds(150);
        if (this.window < TimeSpan.Zero || this.window > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(window));
        worker = Task.Run(RunAsync);
    }

    public Task<ControlResult> SubmitAsync(double value, CancellationToken cancellation = default)
    {
        if (!double.IsFinite(value))
            return Task.FromResult(new ControlResult(ResultCode.InvalidRequest, "Non-finite value"));
        lock (gate)
        {
            if (closing || cancellation.IsCancellationRequested)
                return Task.FromResult(new ControlResult(ResultCode.Cancelled, "Not queued"));
            pending?.Completion.TrySetResult(new(ResultCode.Cancelled, "Superseded before write"));
            var completion = new TaskCompletionSource<ControlResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = new(new(feature, value), cancellation, completion);
            if (signal.CurrentCount == 0)
                signal.Release();
            return completion.Task;
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await signal.WaitAsync();
            await Task.Delay(window);
            Pending? next;
            lock (gate)
            {
                if (closing)
                    return;
                next = pending;
                pending = null;
            }
            if (next is null)
                continue;
            // Controller rechecks identity, capabilities, bounds and cancellation.
            // New slider values never cancel an operation that may have written.
            var result = await controller.ApplyAsync(next.Request, next.Cancellation);
            next.Completion.TrySetResult(result);
        }
    }

    public async ValueTask DisposeAsync()
    {
        bool owner;
        lock (gate)
        {
            owner = !closing;
            closing = true;
            pending?.Completion.TrySetResult(new(ResultCode.Cancelled, "Queue closed before write"));
            pending = null;
            if (owner && signal.CurrentCount == 0)
                signal.Release();
        }
        await worker;
        if (owner)
            signal.Dispose();
    }
}
