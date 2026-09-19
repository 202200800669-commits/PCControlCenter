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
    public int? CurrentHardwareBrightness { get; set; } = null;

    public Task<BrightnessResult> SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        Invocations.Add(percent);
        var result = NextResult.Success ? new BrightnessResult(true, "Success", percent) : NextResult;
        return Task.FromResult(result);
    }

    public Task<int?> GetBrightnessAsync(CancellationToken ct = default) => Task.FromResult(CurrentHardwareBrightness);
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
    public Func<string, CancellationToken, Task<SessionStatus>>? RecoveryStatusWaiter
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

    public Task<SessionStatus> WaitForRecoveryAsync(string requestId, CancellationToken ct = default)
    {
        if (RecoveryStatusWaiter != null)
            return RecoveryStatusWaiter(requestId, ct);
        if (RecoveryWaiter != null)
        {
            return RecoveryWaiter(ct).ContinueWith(t =>
            {
                if (t.IsCanceled || t.IsFaulted)
                    return new SessionStatus(requestId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED", t.Exception?.Message ?? "Cancelled");
                return t.Result
                    ? new SessionStatus(requestId, SessionState.RecoveryCompleted, "UNCONFIRMED")
                    : new SessionStatus(requestId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED");
            }, TaskScheduler.Default);
        }
        return Task.FromResult(new SessionStatus(requestId, SessionState.RecoveryCompleted, "UNCONFIRMED"));
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

        // 11. Review Regression: Unconfirmed Brightness Consumption Chain & Initialization
        await RunUnconfirmedBrightnessTestsAsync(check);

        // 12. Review Regression: Recovery Wait Cancellation Non-Escapes & State Mappings
        await RunRecoveryExceptionAndStatesTestsAsync(check);

        // 13. Review Regression: In-flight Recovery Process Tracking & Write Constraints
        await RunRecoveryProcessTrackerTestsAsync(check);
    }

    private static async Task RunUnconfirmedBrightnessTestsAsync(Action<bool, string> check)
    {
        var fakeBrightness = new FakeBrightnessService();
        var fakeStorage = new FakeProfileStorage();
        var fakeBroker = new FakeBrokerExecutor();
        var vm = new MainViewModel(fakeBrightness, fakeStorage, fakeBroker);
        vm.SetIdentityForTesting(new DeviceIdentity("LENOVO", "21R0", "ThinkBook 16p G6 IAX", "R2CN57WW", "Windows"));

        // 1. SuccessUnconfirmed 亮度设置：不污染 ConfirmedBrightness，不声称已确认
        fakeBrightness.NextResult = new BrightnessResult(false, "SuccessUnconfirmed", null);
        var setRes = await vm.SetBrightnessAsync(73);
        check(!setRes.Success, "SuccessUnconfirmed brightness returns Success=false");
        check(vm.ConfirmedBrightness == null, "ConfirmedBrightness remains null when readback is unavailable");
        check(!vm.LogText.Contains("已确认: 73%"), "log does not claim brightness confirmed 73%");
        check(vm.StatusText.Contains("未读回确认"), "status text explicitly reports unconfirmed readback");

        // 2. 场景应用中的亮度未确认步骤阻断后续风扇调节
        bool fanInvoked = false;
        fakeBroker.Handler = (req, ct) =>
        {
            if (req.Operation == "fan-trial")
                fanInvoked = true;
            return Task.FromResult(new BrokerResponse(BrokerProtocol.Version, req.RequestId, "OK"));
        };
        var unconfProfile = new ProfileItem { Name = "亮度未确认场景", Mode = 0, Brightness = 73, FanKind = "auto" };
        var profileOk = await vm.ApplyProfileAsync(unconfProfile);
        check(!profileOk, "profile application halts when brightness is SuccessUnconfirmed");
        check(!fanInvoked, "fan step never invoked when brightness confirmation fails");
        check(!vm.LogText.Contains("场景配置应用完成: 亮度未确认场景"), "profile never claims completed when brightness unconfirmed");

        // 3. InitializeAsync 的有读回与无读回两种状态
        fakeBrightness.CurrentHardwareBrightness = 65;
        await vm.InitializeAsync();
        check(vm.Brightness == 65 && vm.ConfirmedBrightness == 65, "initialize with hardware brightness populates confirmed brightness 65");

        fakeBrightness.CurrentHardwareBrightness = null;
        await vm.InitializeAsync();
        check(vm.ConfirmedBrightness == null, "initialize without hardware brightness sets confirmed brightness to null");
        check(vm.LogText.Contains("未读取到初始屏幕亮度"), "log records that initial brightness could not be read");
    }

    private static async Task RunRecoveryExceptionAndStatesTestsAsync(Action<bool, string> check)
    {
        var fakeBrightness = new FakeBrightnessService();
        var fakeStorage = new FakeProfileStorage();
        var fakeBroker = new FakeBrokerExecutor();
        var vm = new MainViewModel(fakeBrightness, fakeStorage, fakeBroker);
        vm.SetIdentityForTesting(new DeviceIdentity("LENOVO", "21R0", "ThinkBook 16p G6 IAX", "R2CN57WW", "Windows"));

        // 1. 模拟真实 Task.Delay 取消令牌触发 OperationCanceledException 逃逸路径
        fakeBroker.Handler = (req, ct) => throw new OperationCanceledException();
        fakeBroker.RecoveryWaiter = ct => Task.FromCanceled<bool>(new CancellationToken(true));

        bool escaped = false;
        try
        {
            await vm.RunFanTrialAsync(3500, 3500, 10);
        }
        catch (OperationCanceledException)
        {
            escaped = true;
        }
        check(!escaped, "cancellation exception during recovery wait does not escape to UI caller");
        check(!vm.IsBusy, "vm leaves busy state after recovery wait cancellation");
        check(vm.FanStateText == "恢复未确认", "fan state reports 恢复未确认 instead of leaving 取消中 (恢复中)");

        // 2. 状态映射场景 1: 取消发生在工作者创建锁之前 (SessionState.NotStarted, 零写入)
        fakeBroker.RecoveryStatusWaiter = (reqId, ct) =>
            Task.FromResult(new SessionStatus(reqId, SessionState.NotStarted, "NOT_NEEDED"));
        var notStartedRes = await vm.RunFanTrialAsync(3500, 3500, 10);
        check(!notStartedRes.Success, "not started cancellation returns Success=false");
        check(notStartedRes.Recovery == "NOT_NEEDED", "not started cancellation reports Recovery=NOT_NEEDED");
        check(vm.FanStateText == "已取消 (未启动)", "fan state reports 已取消 (未启动) for pre-lock cancellation");

        // 3. 状态映射场景 2: 正常恢复结束 (SessionState.RecoveryCompleted)
        fakeBroker.RecoveryStatusWaiter = (reqId, ct) =>
            Task.FromResult(new SessionStatus(reqId, SessionState.RecoveryCompleted, "RESTORED_AUTO"));
        var completedRes = await vm.RunFanTrialAsync(3500, 3500, 10);
        check(vm.FanStateText == "已取消 (未确认)", "completed recovery maps to 已取消 (未确认)");
        check(completedRes.Recovery == "RESTORED_AUTO", "recovery receipt restored auto");

        // 4. 状态映射场景 3: 工作者异常退出 / 遗弃互斥锁 (SessionState.TerminatedUnconfirmed)
        fakeBroker.RecoveryStatusWaiter = (reqId, ct) =>
            Task.FromResult(new SessionStatus(reqId, SessionState.TerminatedUnconfirmed, "UNCONFIRMED", "AbandonedMutexException"));
        var terminatedRes = await vm.RunFanTrialAsync(3500, 3500, 10);
        check(!terminatedRes.Success, "terminated worker returns Success=false");
        check(vm.FanStateText == "恢复未确认", "terminated worker maps to 恢复未确认");

        // 5. 状态映射场景 4: 恢复等待超时 (SessionState.TimedOutUnconfirmed)
        fakeBroker.RecoveryStatusWaiter = (reqId, ct) =>
            Task.FromResult(new SessionStatus(reqId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED", "Timeout"));
        var timeoutRes = await vm.RunFanTrialAsync(3500, 3500, 10);
        check(!timeoutRes.Success, "timed out recovery returns Success=false");
        check(vm.FanStateText == "恢复未确认", "timed out recovery maps to 恢复未确认");
    }

    private static async Task RunRecoveryProcessTrackerTestsAsync(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;

        PCControlCenter.Providers.Windows.RecoveryProcessTracker.ClearForTesting();
        check(!PCControlCenter.Providers.Windows.RecoveryProcessTracker.HasInFlightRecovery, "initially no in-flight recovery processes");

        // 注入安全守卫：若任何调用越过预期分支尝试启动真实硬件脚本，立即抛错熔断，绝不触碰生产硬件
        PCControlCenter.Providers.Windows.ModeSessionRunner.ProcessLauncher = psi =>
            throw new InvalidOperationException("SAFETY VIOLATION: Test reached real hardware process launch in ModeSessionRunner!");
        PCControlCenter.Providers.Windows.EnergySessionRunner.ProcessLauncher = psi =>
            throw new InvalidOperationException("SAFETY VIOLATION: Test reached real hardware process launch in EnergySessionRunner!");

        try
        {
            // 启动显式受控的测试子进程
            var psi = new System.Diagnostics.ProcessStartInfo(
                @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                int testPid = proc.Id;
                try
                {
                    PCControlCenter.Providers.Windows.RecoveryProcessTracker.Track(proc, "Global\\PCControlCenter.ThinkBookModeSession");

                    // 1. 模拟调用方在 Track 后执行 Dispose（覆盖报告 P1 反例）
                    proc.Dispose();
                    await Task.Delay(100);

                    // 即使调用方提前 Dispose 了 Process 句柄，只要子进程依然存活，跟踪器必须正确识别存活
                    check(PCControlCenter.Providers.Windows.RecoveryProcessTracker.HasInFlightRecovery, "in-flight recovery remains active after caller disposes Process instance");

                    // 2. 模式切换与能源运行器受到在途恢复约束，直接返回 BUSY
                    var modeReceipt = await PCControlCenter.Providers.Windows.ModeSessionRunner.RunAsync(new ModeRequest(1), CancellationToken.None);
                    check(modeReceipt.Code == "BUSY", "mode session runner rejects writes with BUSY while in-flight recovery is active");

                    var energyReceipt = await PCControlCenter.Providers.Windows.EnergySessionRunner.RunAsync(new EnergyRequest("charge", 1), CancellationToken.None);
                    check(energyReceipt.Code == "BUSY", "energy session runner rejects writes with BUSY while in-flight recovery is active");
                }
                finally
                {
                    // 显式清理测试子进程
                    try
                    {
                        using var live = System.Diagnostics.Process.GetProcessById(testPid);
                        if (!live.HasExited)
                        {
                            live.Kill(true);
                            await live.WaitForExitAsync();
                        }
                    }
                    catch (ArgumentException) { }
                    catch { }

                    PCControlCenter.Providers.Windows.RecoveryProcessTracker.ClearForTesting();
                }

                check(!PCControlCenter.Providers.Windows.RecoveryProcessTracker.HasInFlightRecovery, "recovery tracker cleared after testing");

                // 3. 验收安全隔离：故意在模拟跟踪器清空后调用 RunAsync，断言触发安全熔断器而不是执行硬件代码
                bool safetyTriggered = false;
                try
                {
                    await PCControlCenter.Providers.Windows.ModeSessionRunner.RunAsync(new ModeRequest(1), CancellationToken.None);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("SAFETY VIOLATION"))
                {
                    safetyTriggered = true;
                }
                check(safetyTriggered, "ProcessLauncher safety guard reliably intercepts unconstrained calls and prevents hardware access");
            }
        }
        finally
        {
            // 恢复生产启动器
            PCControlCenter.Providers.Windows.ModeSessionRunner.ProcessLauncher = null;
            PCControlCenter.Providers.Windows.EnergySessionRunner.ProcessLauncher = null;
        }
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
