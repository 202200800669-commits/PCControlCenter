using PCControlCenter.Desktop.ViewModels;
using PCControlCenter.Desktop.Services;

static class DesktopViewModelTests
{
    public static void RunAll(Action<bool, string> check)
    {
        // 1. Initial State & Collections
        var vm = new MainViewModel();
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
        var autoTrial = new PCControlCenter.Core.FanTrial("auto", 0, 0, 0);
        check(autoTrial.IsValid, "auto fan trial requires 0 lease and 0 rpm");
        var badAuto = new PCControlCenter.Core.FanTrial("auto", 1500, 1500, 5);
        check(!badAuto.IsValid, "auto fan trial rejects non-zero rpm or lease");
        var manualTrial = new PCControlCenter.Core.FanTrial("manual", 1500, 1500, 5);
        check(manualTrial.IsValid, "manual fan trial accepts bounded rpm and lease");

        // 7. Brightness Clamping
        vm.SetBrightnessAsync(150).GetAwaiter().GetResult();
        check(vm.Brightness == 100, "brightness is clamped to maximum 100");
        vm.SetBrightnessAsync(-10).GetAwaiter().GetResult();
        check(vm.Brightness == 0, "brightness is clamped to minimum 0");
    }
}
