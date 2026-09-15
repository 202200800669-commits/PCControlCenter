using PCControlCenter.Core;

static class SliderQueueTests
{
    private sealed class Writer(DeviceIdentity identity) : IHardwareProvider
    {
        public string Id => "slider-test";
        public List<double> Values = [];
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Matches(DeviceIdentity device) => true;
        public Task<Snapshot> ReadAsync(DeviceIdentity device, CancellationToken ct) => Task.FromResult(new Snapshot(Id, identity,
            [new(Feature.FanControl, SupportLevel.Verified, true, "RPM", 1500, 5500)], [], []));
        public async Task<ControlResult> ApplyAsync(DeviceIdentity device, ControlRequest request, CancellationToken ct)
        {
            Values.Add(request.Value);
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new(ResultCode.Success, "Simulated");
        }
    }

    public static async Task RunAsync(DeviceIdentity identity, Action<bool, string> check)
    {
        var provider = new Writer(identity);
        await using (var queue = new LatestControlQueue(new Controller(provider, identity), Feature.FanControl, TimeSpan.Zero))
        {
            try
            {
                var first = queue.SubmitAsync(2000);
                await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                var pending = Enumerable.Range(0, 100).Select(i => queue.SubmitAsync(3000 + i)).ToArray();
                check(pending.Take(99).All(t => t.IsCompletedSuccessfully && t.Result.Code == ResultCode.Cancelled), "slider superseded values complete without writes");
                check(provider.Values.SequenceEqual([2000d]), "slider updates do not interrupt the active write");
                check((await queue.SubmitAsync(double.NaN)).Code == ResultCode.InvalidRequest, "invalid slider input does not replace valid pending value");
                provider.Release.SetResult();
                var results = await Task.WhenAll(first, pending[^1]).WaitAsync(TimeSpan.FromSeconds(3));
                check(results.All(r => r.Code == ResultCode.Success) && provider.Values.SequenceEqual([2000d, 3099d]), "slider executes only active and latest pending values");
            }
            finally
            {
                provider.Release.TrySetResult();
            }
        }
        var closingProvider = new Writer(identity);
        var closingQueue = new LatestControlQueue(new Controller(closingProvider, identity), Feature.FanControl, TimeSpan.Zero);
        try
        {
            var active = closingQueue.SubmitAsync(2500);
            await closingProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var pending = closingQueue.SubmitAsync(3500);
            var shutdown = closingQueue.DisposeAsync().AsTask();
            check((await pending).Code == ResultCode.Cancelled && !shutdown.IsCompleted, "queue shutdown cancels pending work but waits for active outcome");
            check((await closingQueue.SubmitAsync(4000)).Code == ResultCode.Cancelled, "closed slider queue rejects new work");
            closingProvider.Release.SetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
            check((await active).Code == ResultCode.Success && closingProvider.Values.SequenceEqual([2500d]), "queue shutdown preserves the active operation result");
        }
        finally
        {
            closingProvider.Release.TrySetResult();
            await closingQueue.DisposeAsync();
        }
    }
}
