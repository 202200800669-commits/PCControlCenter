using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PCControlCenter.Core;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Ipc;
using PCControlCenter.Providers.Windows;

namespace PCControlCenter.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly WindowsProbe probe = new();
    private DeviceIdentity? currentIdentity;
    private string deviceTitle = "ThinkBook 16p G6 IAX";
    private string cpuName = "处理器加载中…";
    private string cpuLoadText = "— %";
    private double cpuLoad;
    private string gpuLoadText = "— %";
    private double gpuLoad;
    private string gpuTempText = "—";
    private string gpuDetailText = "等待显卡数据…";
    private string memoryText = "— / — GB";
    private double memoryLoad;
    private string batteryText = "—";
    private int brightness = 80;
    private int performanceMode = -1;
    private int? fan1Rpm;
    private int? fan2Rpm;
    private string fanText = "需提权读取";
    private string fanStateText = "需提权";
    private int energyCharge = -1;
    private int energyNight = -1;
    private int energyKey = -1;
    private string statusText = "就绪";
    private string logText = "";
    private bool isBusy;
    private string accentColor = "#278FCD";
    private bool minimizeToTray = true;
    private int pollIntervalSeconds = 3;
    private ProfileItem? selectedProfile;

    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public Queue<double> CpuHistory { get; } = new();
    public Queue<double> GpuHistory { get; } = new();
    public Snapshot? CurrentSnapshot { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? GraphUpdated;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string DeviceTitle { get => deviceTitle; set { deviceTitle = value; Notify(); } }
    public string CpuName { get => cpuName; set { cpuName = value; Notify(); } }
    public string CpuLoadText { get => cpuLoadText; set { cpuLoadText = value; Notify(); } }
    public double CpuLoad { get => cpuLoad; set { cpuLoad = value; Notify(); } }
    public string GpuLoadText { get => gpuLoadText; set { gpuLoadText = value; Notify(); } }
    public double GpuLoad { get => gpuLoad; set { gpuLoad = value; Notify(); } }
    public string GpuTempText { get => gpuTempText; set { gpuTempText = value; Notify(); } }
    public string GpuDetailText { get => gpuDetailText; set { gpuDetailText = value; Notify(); } }
    public string MemoryText { get => memoryText; set { memoryText = value; Notify(); } }
    public double MemoryLoad { get => memoryLoad; set { memoryLoad = value; Notify(); } }
    public string BatteryText { get => batteryText; set { batteryText = value; Notify(); } }
    public int Brightness { get => brightness; set { brightness = value; Notify(); } }
    public int PerformanceMode { get => performanceMode; set { performanceMode = value; Notify(); Notify(nameof(PerformanceModeName)); } }
    public string PerformanceModeName => performanceMode switch
    {
        0 => "智能模式 (均衡)",
        1 => "节能模式 (安静)",
        3 => "性能模式 (野兽)",
        _ => "未识别 / 需读取"
    };
    public int? Fan1Rpm { get => fan1Rpm; set { fan1Rpm = value; Notify(); } }
    public int? Fan2Rpm { get => fan2Rpm; set { fan2Rpm = value; Notify(); } }
    public string FanText { get => fanText; set { fanText = value; Notify(); } }
    public string FanStateText { get => fanStateText; set { fanStateText = value; Notify(); } }
    public int EnergyCharge { get => energyCharge; set { energyCharge = value; Notify(); } }
    public int EnergyNight { get => energyNight; set { energyNight = value; Notify(); } }
    public int EnergyKey { get => energyKey; set { energyKey = value; Notify(); } }
    public string StatusText { get => statusText; set { statusText = value; Notify(); } }
    public string LogText { get => logText; set { logText = value; Notify(); } }
    public bool IsBusy { get => isBusy; set { isBusy = value; Notify(); } }
    public string AccentColor { get => accentColor; set { accentColor = value; Notify(); } }
    public bool MinimizeToTray { get => minimizeToTray; set { minimizeToTray = value; Notify(); } }
    public int PollIntervalSeconds { get => pollIntervalSeconds; set { pollIntervalSeconds = value; Notify(); } }
    public ProfileItem? SelectedProfile { get => selectedProfile; set { selectedProfile = value; Notify(); } }

    private const string RunRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunAppName = "PCControlCenter";

    public bool AutoStart
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegKey, false);
                return key?.GetValue(RunAppName) != null;
            }
            catch { return false; }
        }
        set
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegKey, true);
                if (key != null)
                {
                    if (value)
                    {
                        var exePath = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "pc-control-desktop.exe");
                        key.SetValue(RunAppName, $"\"{exePath}\" --minimized");
                        AddLog("已启用开机自动启动（最小化到托盘）");
                    }
                    else
                    {
                        key.DeleteValue(RunAppName, false);
                        AddLog("已禁用开机自动启动");
                    }
                }
                Notify();
            }
            catch (Exception ex)
            {
                AddLog($"设置自启动失败: {ex.Message}");
            }
        }
    }

    public MainViewModel()
    {
        for (int i = 0; i < 60; i++)
        {
            CpuHistory.Enqueue(0);
            GpuHistory.Enqueue(0);
        }
        Profiles.Add(new() { Name = "日常办公", Mode = 0, FanKind = "auto", Brightness = 70 });
        Profiles.Add(new() { Name = "性能优先", Mode = 3, FanKind = "auto", Brightness = 90 });
        Profiles.Add(new() { Name = "全速散热", Mode = 0, FanKind = "full", Brightness = 80 });
        selectedProfile = Profiles[0];
    }

    public void AddLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        LogText = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
        if (LogText.Length > 8000)
        {
            var lines = LogText.Split(Environment.NewLine);
            LogText = string.Join(Environment.NewLine, lines.TakeLast(100));
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            currentIdentity = await WindowsProbe.IdentifyAsync(probe, ct);
            DeviceTitle = $"{currentIdentity.Model}";
            AddLog($"已识别设备: {currentIdentity.Manufacturer} {currentIdentity.Product} ({currentIdentity.Bios})");
        }
        catch (Exception ex)
        {
            currentIdentity = new("LENOVO", "21R0", "ThinkBook 16p G6 IAX", "R2CN57WW", "Windows");
            AddLog($"识别硬件异常: {ex.Message}，采用默认设备指纹");
        }
        try
        {
            var registry = PCControlCenter.Providers.Windows.Providers.Create(probe);
            var provider = registry.Resolve(currentIdentity);
            CurrentSnapshot = await provider.ReadAsync(currentIdentity, ct);
        }
        catch { }
        await RefreshTelemetryAsync(ct);
    }

    public async Task RefreshTelemetryAsync(CancellationToken ct = default)
    {
        try
        {
            // 1. CPU & Memory & Battery
            double cpu = SystemMetrics.ReadCpuLoad();
            CpuLoad = cpu;
            CpuLoadText = $"{cpu:F0}%";
            CpuHistory.Dequeue();
            CpuHistory.Enqueue(cpu);

            var (usedMem, totalMem, memPct) = SystemMetrics.ReadMemory();
            MemoryLoad = memPct;
            MemoryText = totalMem > 0 ? $"{usedMem:F1} / {totalMem:F0} GB" : "—";
            BatteryText = SystemMetrics.ReadBattery();

            // 2. NVIDIA Telemetry
            var gpuData = await NvidiaTelemetry.ReadAsync(ct);
            if (gpuData.Count > 0)
            {
                var primary = gpuData[0];
                if (primary.Temperature.HasValue) GpuTempText = $"{primary.Temperature.Value:F0} °C";
                if (primary.Utilization.HasValue)
                {
                    double gl = primary.Utilization.Value;
                    GpuLoad = gl;
                    GpuLoadText = $"{gl:F0}%";
                    GpuHistory.Dequeue();
                    GpuHistory.Enqueue(gl);
                }
                string pwr = primary.Power.HasValue ? $"{primary.Power.Value:F1} W" : "— W";
                GpuDetailText = $"负载 {primary.Utilization:F0}%  ·  {pwr}  ·  NVIDIA 5060";
            }
            else
            {
                GpuHistory.Dequeue();
                GpuHistory.Enqueue(0);
            }

            // 3. Performance Mode (Non-elevated registry check)
            try
            {
                var modeData = await probe.QueryAsync(ProbeKind.ThinkBookMode, ct);
                if (modeData.TryGetProperty("mode", out var m))
                    PerformanceMode = m.GetInt32();
            }
            catch { }

            // 4. Energy read-only via \\.\EnergyDrv
            int chg = EnergyReader.ReadCharge();
            if (chg >= 0) EnergyCharge = chg;
            int nht = EnergyReader.ReadNight();
            if (nht >= 0) EnergyNight = nht;
            int key = EnergyReader.ReadKeyboard();
            if (key >= 0) EnergyKey = key;

            GraphUpdated?.Invoke();
            StatusText = $"已连接  ·  {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText = $"遥测刷新异常: {ex.Message}";
        }
    }

    public async Task SetPerformanceModeAsync(int targetMode)
    {
        if (IsBusy || currentIdentity is null) return;
        IsBusy = true;
        StatusText = $"正在通过 UAC 请求切换至模式 {targetMode}…";
        AddLog($"请求模式切换 -> {targetMode}");
        try
        {
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "set-mode", currentIdentity, Mode: new ModeRequest(targetMode));
            var resp = await BrokerClient.ExecuteAsync(req, CancellationToken.None);
            if (resp.Code == "OK" && resp.ModeReceipt is { } mr)
            {
                PerformanceMode = mr.TargetMode;
                AddLog($"模式切换成功: 当前模式={mr.TargetMode}, 恢复状态={mr.Recovery}");
                StatusText = $"模式切换完成 ({PerformanceModeName})";
            }
            else
            {
                AddLog($"模式切换未完全成功: Code={resp.Code}, 回执={resp.ModeReceipt?.Recovery}");
                StatusText = $"模式切换提示: {resp.Code}";
            }
        }
        catch (Exception ex)
        {
            AddLog($"模式切换失败: {ex.Message}");
            StatusText = $"操作失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            await RefreshTelemetryAsync();
        }
    }

    public async Task SetEnergyAsync(string kind, int targetValue)
    {
        if (IsBusy || currentIdentity is null) return;
        IsBusy = true;
        StatusText = $"正在请求配置 {kind} = {targetValue}…";
        AddLog($"请求能源设置 -> {kind} = {targetValue}");
        try
        {
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "set-energy", currentIdentity, Energy: new EnergyRequest(kind, targetValue));
            var resp = await BrokerClient.ExecuteAsync(req, CancellationToken.None);
            if (resp.Code == "OK" && resp.EnergyReceipt is { } er)
            {
                if (kind == "charge") EnergyCharge = er.TargetValue;
                else if (kind == "night") EnergyNight = er.TargetValue;
                else if (kind == "key") EnergyKey = er.TargetValue;
                AddLog($"能源设置成功: {kind}={er.TargetValue}, 恢复={er.Recovery}");
                StatusText = $"能源设置成功: {kind}={targetValue}";
            }
            else
            {
                AddLog($"能源设置未完全确认: Code={resp.Code}, 回执={resp.EnergyReceipt?.Recovery}");
                StatusText = $"能源设置结果: {resp.Code}";
            }
        }
        catch (Exception ex)
        {
            AddLog($"能源设置失败: {ex.Message}");
            StatusText = $"操作失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            await RefreshTelemetryAsync();
        }
    }

    public async Task ReadFansElevatedAsync()
    {
        if (IsBusy || currentIdentity is null) return;
        IsBusy = true;
        StatusText = "正在请求管理员提权读取风扇转速…";
        AddLog("请求 UAC 提权读取风扇转速…");
        try
        {
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "read-fans", currentIdentity);
            var resp = await BrokerClient.ExecuteAsync(req, CancellationToken.None);
            if (resp.Code == "OK" && resp.Fan1.HasValue && resp.Fan2.HasValue)
            {
                Fan1Rpm = resp.Fan1.Value;
                Fan2Rpm = resp.Fan2.Value;
                FanText = $"{resp.Fan1.Value} / {resp.Fan2.Value} RPM";
                FanStateText = "已读取";
                AddLog($"风扇转速已更新: Fan1={resp.Fan1.Value} RPM, Fan2={resp.Fan2.Value} RPM");
                StatusText = $"风扇转速已更新 ({FanText})";
            }
            else
            {
                AddLog($"风扇读取回执: {resp.Code}");
                StatusText = $"风扇读取结果: {resp.Code}";
            }
        }
        catch (Exception ex)
        {
            AddLog($"风扇读取失败: {ex.Message}");
            StatusText = $"风扇读取失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RunFanTrialAsync(int rpm1, int rpm2, int durationSeconds)
    {
        if (IsBusy || currentIdentity is null) return;
        IsBusy = true;
        StatusText = $"正在启动限时试运行 ({rpm1}/{rpm2} RPM, {durationSeconds}s)…";
        AddLog($"启动风扇受控试运行: Fan1={rpm1}, Fan2={rpm2}, 持续={durationSeconds}秒");
        try
        {
            var trial = new FanTrial("manual", rpm1, rpm2, durationSeconds);
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "fan-trial", currentIdentity, Trial: trial);
            var resp = await BrokerClient.ExecuteAsync(req, CancellationToken.None);
            FanStateText = "试运行完成";
            AddLog($"风扇试运行结束: Code={resp.Code}, 回执={resp.Receipt?.Recovery}");
            StatusText = $"试运行完成: {resp.Receipt?.Recovery}";
        }
        catch (Exception ex)
        {
            AddLog($"风扇试运行失败: {ex.Message}");
            StatusText = $"试运行失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            await RefreshTelemetryAsync();
        }
    }
}
