using System.Text.Json;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Desktop.ViewModels;
using PCControlCenter.Providers.Windows;

static class TelemetryPerformanceTests
{
    sealed class CountingProbe : IReadOnlyProbe
    {
        public List<ProbeKind> Calls = new();
        public Task<JsonElement> QueryAsync(ProbeKind kind, CancellationToken ct)
        {
            Calls.Add(kind);
            return kind == ProbeKind.BatteryCycles
                ? Task.FromResult(JsonSerializer.SerializeToElement(new
                {
                    cycles = (int?)null
                }))
                : new FakeProbe().QueryAsync(kind, ct);
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var probe = new CountingProbe();
        var pendingRead = new TaskCompletionSource<DeviceControlReadings>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        var vm = new MainViewModel(new FakeBrightnessService(), new FakeProfileStorage(), new FakeBrokerExecutor(),
            telemetryProbe: probe, readControls: _ => { reads++; return pendingRead.Task; });
        vm.SetIdentityForTesting(new("LENOVO", "21R0", "ThinkBook 16p G6 IAX", "R2CN57WW", "Windows"));
        var refresh = vm.RefreshTelemetryAsync();
        check(reads == 1 && !refresh.IsCompleted, "slow telemetry returns control without blocking the caller");
        await vm.RefreshTelemetryAsync();
        check(reads == 1, "overlapping refresh does not queue duplicate hardware reads");
        var changed = vm.SetPerformanceModeAsync(3);
        check(await changed.WaitAsync(TimeSpan.FromSeconds(2)), "confirmed mode change does not wait for pending telemetry");
        pendingRead.SetResult(new(0, 0, 0, 0));
        await refresh;
        check(vm.PerformanceMode == 3, "stale telemetry cannot overwrite a newer confirmed hardware change");
        await vm.RefreshTelemetryAsync();
        check(probe.Calls.SequenceEqual(new[] { ProbeKind.BatteryCycles }), "frequent telemetry skips full inventory and caches battery cycle query");
        check(vm.BatteryDetailText.Contains("循环次数未提供"), "missing battery cycle count is retained as unavailable");
        await vm.GetFreshSnapshotAsync();
        check(probe.Calls.Contains(ProbeKind.Generic), "explicit feedback still obtains a fresh full hardware inventory");
        CheckVisibleUpdates(check);
    }

    private static void CheckVisibleUpdates(Action<bool, string> check)
    {
        Exception? failure = null;
        var results = new List<(bool, string)>();
        var thread = new Thread(() =>
        {
            System.Windows.Window? window = null;
            try
            {
                var view = new System.Windows.Controls.Border();
                window = new System.Windows.Window { Content = view, Width = 100, Height = 100, Left = -20000, Top = -20000, ShowInTaskbar = false };
                var vm = new MainViewModel(new FakeBrightnessService(), new FakeProfileStorage(), new FakeBrokerExecutor());
                int updates = 0;
                PCControlCenter.Desktop.Views.ViewRefresh.Subscribe(view, vm, () => updates++, nameof(vm.Brightness));
                void Drain() => window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.Show();
                Drain();
                updates = 0;
                for (int i = 10; i < 40; i++)
                    vm.Brightness = i;
                Drain();
                results.Add((updates == 1, "visible page coalesces property bursts into one refresh"));
                vm.AddLog("unrelated");
                Drain();
                results.Add((updates == 1, "unrelated notifications do not redraw the page"));
                window.Hide();
                vm.Brightness = 45;
                Drain();
                results.Add((updates == 1, "hidden page skips control updates"));
                window.Show();
                Drain();
                results.Add((updates == 2, "returning to a hidden page refreshes its latest state"));
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
        foreach (var result in results)
            check(result.Item1, result.Item2);
    }
}
