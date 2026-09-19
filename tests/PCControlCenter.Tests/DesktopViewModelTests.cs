using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCControlCenter.Core;
using PCControlCenter.Desktop.ViewModels;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Ipc;

sealed class FakeBrightnessService : IBrightnessService
{
    public List<int> Invocations { get; } = new();
    public BrightnessResult NextResult { get; set; } = new(true, "Success", null);

    public Task<BrightnessResult> SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        Invocations.Add(percent);
        var result = NextResult.Success ? new BrightnessResult(true, "Success", percent) : NextResult;
        return Task.FromResult(result);
    }

    public Task<int?> GetBrightnessAsync(CancellationToken ct = default) => Task.FromResult<int?>(null);
}

sealed class FakeProfileStorage : IProfileStorage
{
    public List<ProfileItem>? StoredProfiles
    {
        get; set;
    }
    public bool ShouldFail
    {
        get; set;
    }

    public FakeProfileStorage(List<ProfileItem>? initial = null)
    {
        StoredProfiles = initial != null ? new List<ProfileItem>(initial) : null;
    }

    public Task<List<ProfileItem>?> LoadProfilesAsync(CancellationToken ct = default)
    {
        if (ShouldFail)
            return Task.FromResult<List<ProfileItem>?>(null);
        return Task.FromResult(StoredProfiles != null ? new List<ProfileItem>(StoredProfiles) : null);
    }

    public Task<bool> SaveProfilesAsync(IReadOnlyList<ProfileItem> profiles, CancellationToken ct = default)
    {
        if (ShouldFail)
            return Task.FromResult(false);
        StoredProfiles = new List<ProfileItem>(profiles);
        return Task.FromResult(true);
    }
}

sealed class FakeBrokerExecutor : IBrokerExecutor
{
    public Func<BrokerRequest, CancellationToken, Task<BrokerResponse>>? Handler
    {
        get; set;
    }
    public Func<CancellationToken, Task<bool>>? RecoveryWaiter
    {
        get; set;
    }
    public List<BrokerRequest> Invocations { get; } = new();

    public Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct)
    {
        Invocations.Add(request);
        if (Handler != null)
            return Handler(request, ct);

        if (request.Operation == "set-mode")
        {
            var receipt = new ModeReceipt("COMPLETED", request.Mode?.Mode ?? 0, Recovery: "NOT_NEEDED");
            return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, request.RequestId, "OK", ModeReceipt: receipt));
        }
        if (request.Operation == "fan-trial")
        {
            var receipt = new FanReceipt("COMPLETED", "NOT_NEEDED");
            return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, request.RequestId, "OK", Receipt: receipt));
        }
        return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, request.RequestId, "OK"));
    }

    public Task<bool> WaitForRecoveryCompleteAsync(CancellationToken ct = default)
    {
        if (RecoveryWaiter != null)
            return RecoveryWaiter(ct);
        return Task.FromResult(true);
    }
}

static class DesktopViewModelTests
{
    public static async Task RunAllAsync(Action<bool, string> check)
    {
        var fakeBrightness = new FakeBrightnessService();
        var fakeStorage = new FakeProfileStorage();
        var fakeBroker = new FakeBrokerExecutor();

        // 1. Initial State & Collections
        var vm = new MainViewModel(fakeBrightness, fakeStorage, fakeBroker);
        check(vm.Profiles.Count == 3, "default profiles count is 3");
        check(vm.SelectedProfile != null && vm.SelectedProfile.Name == "日常办公", "default selected profile is office");
        check(vm.CpuHistory.Count == 60 && vm.GpuHistory.Count == 60, "cpu and gpu history queues initialized to 60 samples");
        check(vm.IsBusy == false, "initial vm is not busy");
        check(vm.PerformanceMode == -1 && vm.PerformanceModeName == "未识别 / 需读取", "initial performance mode is unread");

        // 2. Mode Mapping
        vm.PerformanceMode = 0;
        check(vm.PerformanceModeName == "智能模式 (均衡)", "mode 0 maps to intelligent");
        vm.PerformanceMode = 1;
        check(vm.PerformanceModeName == "节能模式 (安静)", "mode 1 maps to quiet");
        vm.PerformanceMode = 3;
        check(vm.PerformanceModeName == "性能模式 (野兽)", "mode 3 maps to performance");
        vm.PerformanceMode = 99;
        check(vm.PerformanceModeName == "未识别 / 需读取", "invalid mode maps to unread");

        // 3. Log Truncation
        vm.AddLog("test entry");
        check(vm.LogText.Contains("test entry"), "log contains added entry");
        for (int i = 0; i < 200; i++)
            vm.AddLog($"bulk log entry {i}");
        check(vm.LogText.Length <= 8500, "log does not grow unbounded");

        // 4. Safe Readers without Hardware
        int chg = EnergyReader.ReadCharge();
        check(chg is >= -1 and <= 2, "energy charge reader returns bounded or unavailable");
        int night = EnergyReader.ReadNight();
        check(night is -1 or 0 or 1, "energy night reader returns bounded or unavailable");
        int key = EnergyReader.ReadKeyboard();
        check(key is >= -1 and <= 3, "energy keyboard reader returns bounded or unavailable");

        // 5. System Metrics
        double cpu = SystemMetrics.ReadCpuLoad();
        check(cpu is >= 0 and <= 100, "cpu load reader returns bounded percentage");
        var (used, total, pct) = SystemMetrics.ReadMemory();
        check(used >= 0 && total >= 0 && pct >= 0, "memory metrics return non-negative values");
        string bat = SystemMetrics.ReadBattery();
        check(!string.IsNullOrWhiteSpace(bat), "battery text is non-empty");

        // 6. Fan Trial Request Modes
        var autoTrial = new FanTrial("auto", 0, 0, 0);
        check(autoTrial.IsValid, "auto fan trial requires 0 lease and 0 rpm");
        var badAuto = new FanTrial("auto", 1500, 1500, 5);
        check(!badAuto.IsValid, "auto fan trial rejects non-zero rpm or lease");
        var manualTrial = new FanTrial("manual", 1500, 1500, 5);
        check(manualTrial.IsValid, "manual fan trial accepts bounded rpm and lease");

        // 7. Brightness Clamping
        await vm.SetBrightnessAsync(150);
        check(vm.Brightness == 100, "brightness is clamped to maximum 100");
        check(fakeBrightness.Invocations[^1] == 100, "fake brightness service recorded clamped 100");

        await vm.SetBrightnessAsync(-10);
        check(vm.Brightness == 0, "brightness is clamped to minimum 0");
        check(fakeBrightness.Invocations[^1] == 0, "fake brightness service recorded clamped 0");

        // 8. Scenario Failure Short-circuit & Step 3 Fan Validation
        vm.SetIdentityForTesting(new DeviceIdentity("LENOVO", "21R0", "ThinkBook 16p G6 IAX", "R2CN57WW", "Windows"));

        // 8a. Invalid FanKind rejected
        var invalidFanProfile = new ProfileItem { Name = "非法风扇配置", Mode = 0, Brightness = 60, FanKind = "invalid_kind" };
        var invalidFanSuccess = await vm.ApplyProfileAsync(invalidFanProfile);
        check(!invalidFanSuccess, "profile application halts when FanKind is invalid");
        check(vm.LogText.Contains("不支持的风扇模式"), "log explains unsupported fan mode");

        // 8b. Step 1 (Mode) fails
        fakeBroker.Handler = (req, ct) =>
        {
            if (req.Operation == "set-mode")
                return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "OK", ModeReceipt: new ModeReceipt("CONTROL_FAILED", req.Mode?.Mode ?? 0, Recovery: "RESTORED_PREVIOUS")));
            return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "OK"));
        };
        var modeFailProfile = new ProfileItem { Name = "模式失败场景", Mode = 1, Brightness = 60, FanKind = "auto" };
        var modeFailSuccess = await vm.ApplyProfileAsync(modeFailProfile);
        check(!modeFailSuccess, "profile halts when mode switch fails");
        check(vm.LogText.Contains("模式切换至 1 未成功"), "log confirms mode failure stopped scenario");
        check(!vm.LogText.Contains("场景配置应用完成: 模式失败场景"), "profile never claims completed when mode fails");

        // 8c. Step 2 (Brightness) fails
        fakeBroker.Handler = null; // mode succeeds
        fakeBrightness.NextResult = new BrightnessResult(false, "Unsupported", null, "No monitor");
        var brightFailProfile = new ProfileItem { Name = "亮度失败场景", Mode = 0, Brightness = 50, FanKind = "auto" };
        var brightFailSuccess = await vm.ApplyProfileAsync(brightFailProfile);
        check(!brightFailSuccess, "profile halts when brightness fails");
        check(vm.LogText.Contains("亮度设置至 50% 失败"), "log confirms brightness failure stopped scenario");
        check(!vm.LogText.Contains("场景配置应用完成: 亮度失败场景"), "profile never claims completed when brightness fails");

        // 8d. Step 3 (Fan) fails - Broker Error
        fakeBrightness.NextResult = new BrightnessResult(true, "Success", 50);
        fakeBroker.Handler = (req, ct) =>
        {
            if (req.Operation == "fan-trial")
                return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "ERROR"));
            var m = new ModeReceipt("COMPLETED", req.Mode?.Mode ?? 0, Recovery: "NOT_NEEDED");
            return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "OK", ModeReceipt: m));
        };
        var fanFailProfile = new ProfileItem { Name = "风扇失败场景", Mode = 0, Brightness = 50, FanKind = "auto" };
        var fanFailSuccess = await vm.ApplyProfileAsync(fanFailProfile);
        check(!fanFailSuccess, "profile halts and returns false when fan step fails");
        check(!vm.LogText.Contains("场景配置应用完成: 风扇失败场景"), "profile never claims completed when fan fails");

        // 8e. Step 3 (Fan) fails - Cancelled
        fakeBroker.Handler = (req, ct) =>
        {
            if (req.Operation == "fan-trial")
                throw new OperationCanceledException();
            var m = new ModeReceipt("COMPLETED", req.Mode?.Mode ?? 0, Recovery: "NOT_NEEDED");
            return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "OK", ModeReceipt: m));
        };
        var fanCancelProfile = new ProfileItem { Name = "风扇取消场景", Mode = 0, Brightness = 50, FanKind = "full" };
        var fanCancelSuccess = await vm.ApplyProfileAsync(fanCancelProfile);
        check(!fanCancelSuccess, "profile halts and returns false when fan step is cancelled");
        check(!vm.LogText.Contains("场景配置应用完成: 风扇取消场景"), "profile never claims completed when fan cancelled");

        // 8f. Step 3 (Fan) fails - Recovery UNCONFIRMED
        fakeBroker.Handler = (req, ct) =>
        {
            if (req.Operation == "fan-trial")
                return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "OK", Receipt: new FanReceipt("WORKER_TIMEOUT", "UNCONFIRMED")));
            var m = new ModeReceipt("COMPLETED", req.Mode?.Mode ?? 0, Recovery: "NOT_NEEDED");
            return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "OK", ModeReceipt: m));
        };
        var fanUnconfProfile = new ProfileItem { Name = "风扇未确认场景", Mode = 0, Brightness = 50, FanKind = "auto" };
        var fanUnconfSuccess = await vm.ApplyProfileAsync(fanUnconfProfile);
        check(!fanUnconfSuccess, "profile halts and returns false when fan recovery is unconfirmed");
        check(!vm.LogText.Contains("场景配置应用完成: 风扇未确认场景"), "profile never claims completed when fan unconfirmed");

        // 8g. Step 3 (Fan) success - auto and full paths
        fakeBroker.Handler = null; // all steps succeed
        var fanAutoProfile = new ProfileItem { Name = "日常办公", Mode = 0, Brightness = 70, FanKind = "auto" };
        var fanAutoSuccess = await vm.ApplyProfileAsync(fanAutoProfile);
        check(fanAutoSuccess, "profile succeeds when all 3 steps pass for auto fan");
        check(vm.LogText.Contains("场景配置应用完成: 日常办公"), "profile reports completed for valid auto profile");

        var fanFullProfile = new ProfileItem { Name = "全速散热", Mode = 0, Brightness = 80, FanKind = "full" };
        var fanFullSuccess = await vm.ApplyProfileAsync(fanFullProfile);
        check(fanFullSuccess, "profile succeeds when all 3 steps pass for full fan");
        check(vm.LogText.Contains("场景配置应用完成: 全速散热"), "profile reports completed for valid full profile");

        // 9. Cancellation & Recovery Lifecycle Tracking
        var recoveryTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeBroker.Handler = (req, ct) => throw new OperationCanceledException();
        fakeBroker.RecoveryWaiter = ct => recoveryTcs.Task;

        var cancelTrialTask = vm.RunFanTrialAsync(3500, 3500, 10);
        await Task.Delay(50);
        check(vm.IsBusy, "vm remains busy while waiting for fan recovery after cancellation");
        check(vm.FanStateText.Contains("恢复中"), "fan state reflects recovery in progress");

        var secondOp = await vm.RestoreFanAutoAsync();
        check(!secondOp.Success && secondOp.Status == "Busy", "conflicting fan operations rejected while recovery in progress");

        recoveryTcs.SetResult(true);
        var cancelResult = await cancelTrialTask;
        check(!cancelResult.Success && cancelResult.Status == "Cancelled", "cancelled fan trial returns Cancelled");
        check(!vm.IsBusy, "vm leaves busy state once recovery is confirmed");
        check(vm.FanStateText == "已取消 (未确认)", "fan state shows cancelled unconfirmed after lock release");

        // Recovery Timeout / Unconfirmed
        var unconfirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeBroker.RecoveryWaiter = ct => unconfirmedTcs.Task;
        var unconfTrialTask = vm.RunFanTrialAsync(4000, 4000, 10);
        await Task.Delay(50);
        check(vm.IsBusy, "vm busy during second cancel recovery");
        unconfirmedTcs.SetResult(false);
        var unconfResult = await unconfTrialTask;
        check(!unconfResult.Success && unconfResult.Recovery == "UNCONFIRMED", "unconfirmed recovery reported");
        check(!vm.IsBusy, "vm leaves busy state even if recovery timed out");
        check(vm.FanStateText == "恢复未确认", "fan state explicitly reports unconfirmed");

        // 10. Real Coalescing Queue Tests in WindowsBrightnessService
        await RunBrightnessQueueTestsAsync(check);
    }

    private static async Task RunBrightnessQueueTestsAsync(Action<bool, string> check)
    {
        var executedValues = new List<int>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var queueService = new WindowsBrightnessService(async (val, ct) =>
        {
            lock (executedValues)
                executedValues.Add(val);
            if (val == 10)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(ct);
            }
            return new BrightnessResult(true, "Success", val);
        });

        // Submit 10 (starts executing)
        var t10 = queueService.SetBrightnessAsync(10);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Submit 20, 30, 40, 50 while 10 is executing
        var t20 = queueService.SetBrightnessAsync(20);
        var t30 = queueService.SetBrightnessAsync(30);
        var t40 = queueService.SetBrightnessAsync(40);
        var t50 = queueService.SetBrightnessAsync(50);

        // 20, 30, 40 are superseded
        var r20 = await t20;
        var r30 = await t30;
        var r40 = await t40;
        check(!r20.Success && r20.Status == "Cancelled", "superseded brightness 20 cancelled");
        check(!r30.Success && r30.Status == "Cancelled", "superseded brightness 30 cancelled");
        check(!r40.Success && r40.Status == "Cancelled", "superseded brightness 40 cancelled");

        releaseFirst.SetResult();
        var r10 = await t10;
        var r50 = await t50;
        check(r10.Success && r10.ConfirmedValue == 10, "first brightness 10 completed");
        check(r50.Success && r50.ConfirmedValue == 50, "latest brightness 50 completed");
        lock (executedValues)
        {
            check(executedValues.SequenceEqual(new[] { 10, 50 }), "only active and latest values executed by brightness queue");
        }

        // Pre-cancellation test
        using var preCts = new CancellationTokenSource();
        preCts.Cancel();
        var preCancelled = await queueService.SetBrightnessAsync(60, preCts.Token);
        check(!preCancelled.Success && preCancelled.Status == "Cancelled", "pre-cancelled brightness returns Cancelled without execution");

        // In-queue cancellation test
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queueService2 = new WindowsBrightnessService(async (val, ct) =>
        {
            slowStarted.TrySetResult();
            await releaseSlow.Task.WaitAsync(ct);
            return new BrightnessResult(true, "Success", val);
        });

        var tSlow = queueService2.SetBrightnessAsync(70);
        await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var pendingCts = new CancellationTokenSource();
        var tPending = queueService2.SetBrightnessAsync(80, pendingCts.Token);
        pendingCts.Cancel();
        var rPending = await tPending;
        check(!rPending.Success && rPending.Status == "Cancelled", "in-queue cancelled brightness returns Cancelled immediately");
        releaseSlow.SetResult();
        await tSlow;
    }
}
