using PCControlCenter.Core;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Desktop.ViewModels;
using PCControlCenter.Ipc;
using Forms = System.Windows.Forms;

static class HardwareHandoffTests
{
    sealed class FakeManual : IManualFanSession
    {
        public bool IsAuthorized => true;
        public List<(int, int)> Targets = new();
        public int Stops;
        public bool Confirm = true;
        public TaskCompletionSource? RecoveryGate;
        public async Task<BrokerResponse> RunAsync(BrokerRequest request, Func<(int First, int Second)> target, Action<BrokerResponse> update, CancellationToken ct)
        {
            Targets.Add(target());
            update(new(2, request.RequestId, "RUNNING", request.Trial!.Rpm1, request.Trial.Rpm2));
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) { }
            Stops++;
            if (RecoveryGate is not null)
                await RecoveryGate.Task;
            return new(2, request.RequestId, "OK", Receipt: new("INTERRUPTED", Confirm ? "AUTO_COMMANDS_SENT_OVERRIDE_OFF" : "UNCONFIRMED"));
        }
    }
    static MainViewModel Create(FakeManual live, FakeBrokerExecutor broker)
    {
        var vm = new MainViewModel(new FakeBrightnessService(), new FakeProfileStorage(), broker, live);
        vm.SetIdentityForTesting(new("LENOVO", "21R0", "ThinkBook 16p G6 IAX", "R2CN57WW", "Windows"));
        vm.EnergyKey = 0;
        return vm;
    }
    static BrokerResponse Energy(BrokerRequest r) => new(2, r.RequestId, "OK", EnergyReceipt: new("COMPLETED", r.Energy!.Kind, r.Energy.Value, FinalValue: r.Energy.Value, Recovery: "NOT_NEEDED"));
    static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition())
            await Task.Delay(20, timeout.Token);
    }
    public static async Task RunAsync(Action<bool, string> check)
    {
        var live = new FakeManual();
        var broker = new FakeBrokerExecutor();
        bool wroteAfterStop = false;
        broker.Handler = (r, _) => { wroteAfterStop = live.Stops > 0; return Task.FromResult(r.Operation == "set-energy" ? Energy(r) : new BrokerResponse(2, r.RequestId, "OK", ModeReceipt: new("COMPLETED", r.Mode!.Mode, Recovery: "NOT_NEEDED"))); };
        var vm = Create(live, broker);
        await vm.SetManualFanTargetAsync(4000, 4200);
        check(vm.ManualFanActive && vm.CanSwitchHardware, "manual fan session keeps mode and energy controls available");
        await vm.SetEnergyAsync("key", 1);
        check(wroteAfterStop && vm.ManualFanActive && live.Targets.SequenceEqual(new[] { (4000, 4200), (4000, 4200) }), "energy change waits for recovery then resumes both original manual targets");
        int before = live.Stops;
        await vm.SetEnergyAsync("key", vm.EnergyKey);
        check(live.Stops == before, "redundant energy selection does not interrupt manual cooling");
        check(await vm.SetPerformanceModeAsync(3) && !vm.ManualFanActive && live.Targets.Count == 2, "performance change leaves fan automatic without silently reapplying manual override");

        live = new FakeManual { Confirm = false };
        broker = new FakeBrokerExecutor();
        vm = Create(live, broker);
        await vm.SetManualFanTargetAsync(4000, 4100);
        await vm.SetEnergyAsync("key", 1);
        check(broker.Invocations.Count == 0 && !vm.ManualFanActive && !vm.HardwareTransition, "unconfirmed fan recovery prevents the next energy write and clears transition state");

        live = new FakeManual { RecoveryGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        broker = new FakeBrokerExecutor { Handler = (r, _) => Task.FromResult(Energy(r)) };
        vm = Create(live, broker);
        await vm.SetManualFanTargetAsync(4000, 4100);
        var pending = vm.SetEnergyAsync("key", 1);
        await Until(() => live.Stops == 1);
        await vm.SetPerformanceModeAsync(1);
        await vm.SetManualFanTargetAsync(4500, 4500);
        check(vm.HardwareTransition && !vm.CanSwitchHardware && broker.Invocations.Count == 0 && live.Targets.Count == 1, "recovery wait blocks duplicate writes and slider restarts");
        vm.CancelManualResume();
        live.RecoveryGate.SetResult();
        await pending;
        check(broker.Invocations.Count == 1 && !vm.ManualFanActive && live.Targets.Count == 1, "closing or explicit auto request cancels a pending manual resume");

        live = new FakeManual();
        broker = new FakeBrokerExecutor { Handler = (r, _) => Task.FromResult(new BrokerResponse(2, r.RequestId, "WORKER_FAILED")) };
        vm = Create(live, broker);
        await vm.SetManualFanTargetAsync(4000, 4100);
        await vm.SetEnergyAsync("key", 1);
        check(!vm.ManualFanActive && live.Targets.Count == 1 && vm.EnergyStatus.Contains("WORKER_FAILED"), "failed energy change stays automatic and reports the failure");

        check(SystemMetrics.DescribeBatteryState(Forms.BatteryChargeStatus.Unknown, Forms.PowerLineStatus.Unknown, 1.5f) == "充电状态未知", "unknown battery flags never imply charging or no battery");
        check(SystemMetrics.DescribeBatteryState(Forms.BatteryChargeStatus.High, Forms.PowerLineStatus.Online, 1f) == "已充满", "full battery on AC is distinguished from charging");
        check(SystemMetrics.DescribeBatteryState(Forms.BatteryChargeStatus.High, Forms.PowerLineStatus.Online, .8f) == "已接电 · 未充电", "AC at a charge limit is not mislabeled as charging");
        check(SystemMetrics.DescribeBatteryState(Forms.BatteryChargeStatus.Charging, Forms.PowerLineStatus.Online, .8f) == "正在充电", "charging flag displays actual charging state");
        check(SystemMetrics.DescribeBatteryState(Forms.BatteryChargeStatus.NoSystemBattery, Forms.PowerLineStatus.Online, 1f) == "未检测到电池", "desktop without battery is not shown as a full battery");
    }
}
