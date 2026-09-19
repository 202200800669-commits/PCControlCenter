using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private readonly IBrightnessService brightnessService;
    private readonly IProfileStorage profileStorage;
    private readonly IBrokerExecutor brokerExecutor;
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
    private CancellationTokenSource? activeFanTrialCts;
    private DateTime lastErrorTimestamp = DateTime.MinValue;
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    public bool IsThinkBookSupported => currentIdentity is not null && new ThinkBookProvider(probe).Matches(currentIdentity);

    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public Queue<double> CpuHistory { get; } = new();
    public Queue<double> GpuHistory { get; } = new();
    public Snapshot? CurrentSnapshot
    {
        get; private set;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? GraphUpdated;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string DeviceTitle
    {
        get => deviceTitle; set
        {
            deviceTitle = value;
            Notify();
        }
    }
    public string CpuName
    {
        get => cpuName; set
        {
            cpuName = value;
            Notify();
        }
    }
    public string CpuLoadText
    {
        get => cpuLoadText; set
        {
            cpuLoadText = value;
            Notify();
        }
    }
    public double CpuLoad
    {
        get => cpuLoad; set
        {
            cpuLoad = value;
            Notify();
        }
    }
    public string GpuLoadText
    {
        get => gpuLoadText; set
        {
            gpuLoadText = value;
            Notify();
        }
    }
    public double GpuLoad
    {
        get => gpuLoad; set
        {
            gpuLoad = value;
            Notify();
        }
    }
    public string GpuTempText
    {
        get => gpuTempText; set
        {
            gpuTempText = value;
            Notify();
        }
    }
    public string GpuDetailText
    {
        get => gpuDetailText; set
        {
            gpuDetailText = value;
            Notify();
        }
    }
    public string MemoryText
    {
        get => memoryText; set
        {
            memoryText = value;
            Notify();
        }
    }
    public double MemoryLoad
    {
        get => memoryLoad; set
        {
            memoryLoad = value;
            Notify();
        }
    }
    public string BatteryText
    {
        get => batteryText; set
        {
            batteryText = value;
            Notify();
        }
    }
    public int Brightness
    {
        get => brightness; set
        {
            brightness = value;
            Notify();
        }
    }
    public int PerformanceMode
    {
        get => performanceMode; set
        {
            performanceMode = value;
            Notify();
            Notify(nameof(PerformanceModeName));
        }
    }
    public string PerformanceModeName => performanceMode switch
    {
        0 => "智能模式 (均衡)",
        1 => "节能模式 (安静)",
        3 => "性能模式 (野兽)",
        _ => "未识别 / 需读取"
    };
    public int? Fan1Rpm
    {
        get => fan1Rpm; set
        {
            fan1Rpm = value;
            Notify();
        }
    }
    public int? Fan2Rpm
    {
        get => fan2Rpm; set
        {
            fan2Rpm = value;
            Notify();
        }
    }
    public string FanText
    {
        get => fanText; set
        {
            fanText = value;
            Notify();
        }
    }
    public string FanStateText
    {
        get => fanStateText; set
        {
            fanStateText = value;
            Notify();
        }
    }
    public int EnergyCharge
    {
        get => energyCharge; set
        {
            energyCharge = value;
            Notify();
        }
    }
    public int EnergyNight
    {
        get => energyNight; set
        {
            energyNight = value;
            Notify();
        }
    }
    public int EnergyKey
    {
        get => energyKey; set
        {
            energyKey = value;
            Notify();
        }
    }
    public string StatusText
    {
        get => statusText; set
        {
            statusText = value;
            Notify();
        }
    }
    public string LogText
    {
        get => logText; set
        {
            logText = value;
            Notify();
        }
    }
    public bool IsBusy
    {
        get => isBusy; set
        {
            isBusy = value;
            Notify();
        }
    }
    public string AccentColor
    {
        get => accentColor; set
        {
            accentColor = value;
            Notify();
        }
    }
    public bool MinimizeToTray
    {
        get => minimizeToTray; set
        {
            minimizeToTray = value;
            Notify();
        }
    }
    public int PollIntervalSeconds
    {
        get => pollIntervalSeconds; set
        {
            pollIntervalSeconds = value;
            Notify();
        }
    }
    public ProfileItem? SelectedProfile
    {
        get => selectedProfile; set
        {
            selectedProfile = value;
            Notify();
        }
    }

    private const string RunRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunAppName = "PCControlCenter";

    public bool AutoStart
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegKey, false);
                return key?.GetValue(RunAppName) != null;
            }
            catch { return false; }
        }
        set
        {
            if (!OperatingSystem.IsWindows())
                return;
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

    public MainViewModel() : this(new WindowsBrightnessService(), new JsonProfileStorage(), new RealBrokerExecutor())
    {
    }

    public MainViewModel(IBrightnessService brightnessService, IProfileStorage profileStorage) : this(brightnessService, profileStorage, new RealBrokerExecutor())
    {
    }

    public MainViewModel(IBrightnessService brightnessService, IProfileStorage profileStorage, IBrokerExecutor brokerExecutor)
    {
        this.brightnessService = brightnessService;
        this.profileStorage = profileStorage;
        this.brokerExecutor = brokerExecutor;
        for (int i = 0; i < 60; i++)
        {
            CpuHistory.Enqueue(0);
            GpuHistory.Enqueue(0);
        }
        PopulateDefaultProfiles();
    }

    public void SetIdentityForTesting(DeviceIdentity identity)
    {
        currentIdentity = identity;
        DeviceTitle = identity.Model;
    }

    public async Task<bool> SaveProfilesAsync(CancellationToken ct = default)
    {
        try
        {
            var ok = await profileStorage.SaveProfilesAsync(Profiles.ToList(), ct);
            if (ok)
            {
                AddLog("场景配置已持久化至本地存储");
                return true;
            }
            AddLog("保存场景配置失败: 存储介质写入未成功");
            return false;
        }
        catch (Exception ex)
        {
            AddLog($"保存场景配置失败: {ex.Message}");
            return false;
        }
    }

    private void PopulateDefaultProfiles()
    {
        Profiles.Clear();
        Profiles.Add(new()
        {
            Name = "日常办公",
            Mode = 0,
            FanKind = "auto",
            Brightness = 70
        });
        Profiles.Add(new()
        {
            Name = "性能优先",
            Mode = 3,
            FanKind = "auto",
            Brightness = 90
        });
        Profiles.Add(new()
        {
            Name = "全速散热",
            Mode = 0,
            FanKind = "full",
            Brightness = 80
        });
        SelectedProfile = Profiles[0];
    }

    public async Task LoadProfilesAsync(CancellationToken ct = default)
    {
        try
        {
            var items = await profileStorage.LoadProfilesAsync(ct);
            if (items != null && items.Count > 0)
            {
                Profiles.Clear();
                foreach (var item in items)
                    Profiles.Add(item);
                SelectedProfile = Profiles[0];
                return;
            }
        }
        catch { }

        PopulateDefaultProfiles();
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
        await LoadProfilesAsync(ct);
        try
        {
            currentIdentity = await WindowsProbe.IdentifyAsync(probe, ct);
            DeviceTitle = $"{currentIdentity.Model}";
            AddLog($"已识别设备: {currentIdentity.Manufacturer} {currentIdentity.Product} ({currentIdentity.Bios})");
        }
        catch (Exception ex)
        {
            currentIdentity = new("Unknown", "GenericPC", "通用计算机 (未匹配参考机)", "Unknown", "Windows");
            DeviceTitle = currentIdentity.Model;
            AddLog($"识别硬件异常: {ex.Message}，采用通用只读模式");
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

    public async Task<Snapshot> GetFreshSnapshotAsync(CancellationToken ct = default)
    {
        if (currentIdentity != null)
        {
            try
            {
                var registry = PCControlCenter.Providers.Windows.Providers.Create(probe);
                var provider = registry.Resolve(currentIdentity);
                var fresh = await provider.ReadAsync(currentIdentity, ct);
                CurrentSnapshot = fresh;
                return fresh;
            }
            catch { }
        }
        return CurrentSnapshot ?? new Snapshot("unknown", currentIdentity ?? new("Unknown", "GenericPC", "PC", "Unknown", "Windows"), [], [], []);
    }

    public async Task<BrightnessResult> SetBrightnessAsync(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        AddLog($"设置屏幕亮度 -> {percent}%");
        try
        {
            var res = await brightnessService.SetBrightnessAsync(percent);
            if (res.Success)
            {
                Brightness = res.ConfirmedValue ?? percent;
                StatusText = $"屏幕亮度已调整为 {Brightness}%";
                AddLog($"屏幕亮度已确认: {Brightness}%");
            }
            else
            {
                StatusText = $"设置亮度失败: {res.Status}";
                AddLog($"设置屏幕亮度失败: [{res.Status}] {res.ErrorMessage}");
                lastErrorTimestamp = DateTime.UtcNow;
            }
            return res;
        }
        catch (Exception ex)
        {
            AddLog($"设置屏幕亮度异常: {ex.Message}");
            StatusText = $"设置亮度异常: {ex.Message}";
            lastErrorTimestamp = DateTime.UtcNow;
            return new BrightnessResult(false, "Failed", null, ex.Message);
        }
    }

    public async Task RefreshTelemetryAsync(CancellationToken ct = default)
    {
        if (!await refreshGate.WaitAsync(0, ct))
            return;
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
                if (primary.Temperature.HasValue)
                    GpuTempText = $"{primary.Temperature.Value:F0} °C";
                else
                    GpuTempText = "—";
                if (primary.Utilization.HasValue)
                {
                    double gl = primary.Utilization.Value;
                    GpuLoad = gl;
                    GpuLoadText = $"{gl:F0}%";
                    GpuHistory.Dequeue();
                    GpuHistory.Enqueue(gl);
                }
                else
                {
                    GpuLoadText = "—";
                    GpuHistory.Dequeue();
                    GpuHistory.Enqueue(0);
                }
                string pwr = primary.Power.HasValue ? $"{primary.Power.Value:F1} W" : "— W";
                GpuDetailText = $"负载 {primary.Utilization:F0}%  ·  {pwr}  ·  NVIDIA GPU (Index {primary.Index})";
            }
            else
            {
                GpuLoadText = "—";
                GpuTempText = "—";
                GpuDetailText = "未检测到独立显卡或处于深度休眠";
                GpuHistory.Dequeue();
                GpuHistory.Enqueue(0);
            }

            // 3. Performance Mode & Energy (Only on supported ThinkBook)
            if (IsThinkBookSupported)
            {
                try
                {
                    var modeData = await probe.QueryAsync(ProbeKind.ThinkBookMode, ct);
                    if (modeData.TryGetProperty("mode", out var m))
                        PerformanceMode = m.GetInt32();
                }
                catch { }

                int chg = EnergyReader.ReadCharge();
                if (chg >= 0)
                    EnergyCharge = chg;
                int nht = EnergyReader.ReadNight();
                if (nht >= 0)
                    EnergyNight = nht;
                int key = EnergyReader.ReadKeyboard();
                if (key >= 0)
                    EnergyKey = key;
            }

            if (currentIdentity != null)
            {
                try
                {
                    var registry = PCControlCenter.Providers.Windows.Providers.Create(probe);
                    var provider = registry.Resolve(currentIdentity);
                    CurrentSnapshot = await provider.ReadAsync(currentIdentity, ct);
                }
                catch { }
            }

            GraphUpdated?.Invoke();
            if (DateTime.UtcNow - lastErrorTimestamp > TimeSpan.FromSeconds(5))
            {
                StatusText = $"已连接  ·  {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"遥测刷新异常: {ex.Message}";
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public async Task<bool> SetPerformanceModeAsync(int targetMode)
    {
        if (IsBusy || currentIdentity is null)
            return false;
        IsBusy = true;
        StatusText = $"正在通过 UAC 请求切换至模式 {targetMode}…";
        AddLog($"请求模式切换 -> {targetMode}");
        bool success = false;
        try
        {
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "set-mode", currentIdentity, Mode: new ModeRequest(targetMode));
            var resp = await brokerExecutor.ExecuteAsync(req, CancellationToken.None);
            if (resp.Code == "OK" && resp.ModeReceipt is { } mr)
            {
                if (mr.Code == "COMPLETED")
                {
                    PerformanceMode = mr.FinalMode ?? mr.TargetMode;
                    AddLog($"模式切换成功: 当前模式={PerformanceMode}, 恢复状态={mr.Recovery}");
                    StatusText = $"模式切换完成 ({PerformanceModeName})";
                    success = true;
                }
                else
                {
                    if (mr.FinalMode.HasValue)
                        PerformanceMode = mr.FinalMode.Value;
                    else if (mr.PreviousMode.HasValue)
                        PerformanceMode = mr.PreviousMode.Value;

                    AddLog($"模式切换未成功: Code={mr.Code}, 恢复={mr.Recovery}, 当前模式={PerformanceMode}");
                    StatusText = $"模式切换未成功 ({mr.Code}), 恢复: {mr.Recovery}";
                    lastErrorTimestamp = DateTime.UtcNow;
                }
            }
            else
            {
                AddLog($"模式切换失败: BrokerCode={resp.Code}");
                StatusText = $"模式切换失败: {resp.Code}";
                lastErrorTimestamp = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            AddLog($"模式切换失败: {ex.Message}");
            StatusText = $"操作失败: {ex.Message}";
            lastErrorTimestamp = DateTime.UtcNow;
        }
        finally
        {
            IsBusy = false;
            await RefreshTelemetryAsync();
        }
        return success;
    }

    public async Task<bool> ApplyProfileAsync(ProfileItem p)
    {
        AddLog($"正在应用场景配置: {p.Name} (模式={p.Mode}, 亮度={p.Brightness}, 风扇={p.FanKind})");

        // 校验 FanKind 必须属于受支持范围
        if (p.FanKind is not ("auto" or "full"))
        {
            AddLog($"场景应用拒绝: 不支持的风扇模式 '{p.FanKind}'");
            StatusText = $"场景应用失败: 不支持的风扇模式 '{p.FanKind}'";
            lastErrorTimestamp = DateTime.UtcNow;
            return false;
        }

        // 步骤 1：性能模式
        var modeOk = await SetPerformanceModeAsync(p.Mode);
        if (!modeOk)
        {
            AddLog($"场景应用中止: 模式切换至 {p.Mode} 未成功，停止执行亮度与风扇调节");
            StatusText = "场景应用中止: 性能模式切换未确认";
            return false;
        }

        // 步骤 2：屏幕亮度
        var brightRes = await SetBrightnessAsync(p.Brightness);
        if (!brightRes.Success)
        {
            AddLog($"场景应用中止: 亮度设置至 {p.Brightness}% 失败 ({brightRes.Status})，停止执行风扇调节");
            StatusText = "场景应用中止: 亮度设置未成功";
            return false;
        }

        // 步骤 3：风扇控制
        FanOperationResult fanRes;
        if (p.FanKind == "full")
        {
            // 使用真正的 full 模式和 30 秒受控试运行语义
            fanRes = await RunFanTrialAsync("full", 0, 0, 30);
        }
        else
        {
            fanRes = await RestoreFanAutoAsync();
        }

        if (!fanRes.Success)
        {
            AddLog($"场景应用中止: 风扇调节未成功 ({fanRes.Status}, Recovery={fanRes.Recovery})");
            StatusText = $"场景应用中止: 风扇调节未确认 ({fanRes.Status})";
            return false;
        }

        AddLog($"场景配置应用完成: {p.Name}");
        StatusText = $"已应用场景配置: {p.Name}";
        return true;
    }

    public async Task SetEnergyAsync(string kind, int targetValue)
    {
        if (IsBusy || currentIdentity is null)
            return;
        IsBusy = true;
        StatusText = $"正在请求配置 {kind} = {targetValue}…";
        AddLog($"请求能源设置 -> {kind} = {targetValue}");
        try
        {
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "set-energy", currentIdentity, Energy: new EnergyRequest(kind, targetValue));
            var resp = await brokerExecutor.ExecuteAsync(req, CancellationToken.None);
            if (resp.Code == "OK" && resp.EnergyReceipt is { } er)
            {
                if (er.Code == "COMPLETED")
                {
                    int finalVal = er.FinalValue ?? er.TargetValue;
                    if (kind == "charge")
                        EnergyCharge = finalVal;
                    else if (kind == "night")
                        EnergyNight = finalVal;
                    else if (kind == "key")
                        EnergyKey = finalVal;
                    AddLog($"能源设置成功: {kind}={finalVal}, 恢复={er.Recovery}");
                    StatusText = $"能源设置成功: {kind}={finalVal}";
                }
                else
                {
                    int? fallbackVal = er.FinalValue ?? er.PreviousValue;
                    if (fallbackVal.HasValue)
                    {
                        if (kind == "charge")
                            EnergyCharge = fallbackVal.Value;
                        else if (kind == "night")
                            EnergyNight = fallbackVal.Value;
                        else if (kind == "key")
                            EnergyKey = fallbackVal.Value;
                    }
                    AddLog($"能源设置未确认: Code={er.Code}, 恢复={er.Recovery}, 当前值={fallbackVal}");
                    StatusText = $"能源设置未确认 ({er.Code}), 恢复: {er.Recovery}";
                    lastErrorTimestamp = DateTime.UtcNow;
                }
            }
            else
            {
                AddLog($"能源设置未完全确认: Code={resp.Code}, 回执={resp.EnergyReceipt?.Recovery}");
                StatusText = $"能源设置结果: {resp.Code}";
                lastErrorTimestamp = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            AddLog($"能源设置失败: {ex.Message}");
            StatusText = $"操作失败: {ex.Message}";
            lastErrorTimestamp = DateTime.UtcNow;
        }
        finally
        {
            IsBusy = false;
            await RefreshTelemetryAsync();
        }
    }

    public async Task ReadFansElevatedAsync()
    {
        if (IsBusy || currentIdentity is null)
            return;
        IsBusy = true;
        StatusText = "正在请求管理员提权读取风扇转速…";
        AddLog("请求 UAC 提权读取风扇转速…");
        try
        {
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "read-fans", currentIdentity);
            var resp = await brokerExecutor.ExecuteAsync(req, CancellationToken.None);
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
                lastErrorTimestamp = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            AddLog($"风扇读取失败: {ex.Message}");
            StatusText = $"风扇读取失败: {ex.Message}";
            lastErrorTimestamp = DateTime.UtcNow;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<FanOperationResult> RestoreFanAutoAsync()
    {
        if (activeFanTrialCts != null)
        {
            AddLog("正在中止当前风扇试运行并恢复固件自动控制…");
            StatusText = "正在中止试运行并请求恢复固件自动控制…";
            activeFanTrialCts.Cancel();
            return new FanOperationResult(false, "Busy", Message: "正在中止先前试运行");
        }

        if (IsBusy || currentIdentity is null)
            return new FanOperationResult(false, "Busy", Message: "系统忙碌或未识别");

        IsBusy = true;
        StatusText = "正在请求固件恢复风扇自动控制…";
        AddLog("请求风扇恢复自动模式 (auto)");
        FanOperationResult result;
        try
        {
            var trial = new FanTrial("auto", 0, 0, 0);
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "fan-trial", currentIdentity, Trial: trial);
            var resp = await brokerExecutor.ExecuteAsync(req, CancellationToken.None);
            var uiStatus = FanStatusMapper.MapRestoreAuto(resp, cancelled: false);
            FanStateText = uiStatus.FanStateText;
            StatusText = uiStatus.StatusText;
            AddLog($"恢复自动回执: {uiStatus.StatusText}");
            if (uiStatus.IsError)
            {
                lastErrorTimestamp = DateTime.UtcNow;
                result = new FanOperationResult(false, uiStatus.FanStateText, resp.Receipt?.Recovery, uiStatus.StatusText);
            }
            else
            {
                result = new FanOperationResult(true, "Completed", resp.Receipt?.Recovery, uiStatus.StatusText);
            }
        }
        catch (Exception ex)
        {
            FanStateText = "恢复异常";
            AddLog($"恢复自动异常: {ex.Message}");
            StatusText = $"恢复自动失败: {ex.Message}";
            lastErrorTimestamp = DateTime.UtcNow;
            result = new FanOperationResult(false, "Failed", null, ex.Message);
        }
        finally
        {
            IsBusy = false;
            await RefreshTelemetryAsync();
        }
        return result;
    }

    public Task<FanOperationResult> RunFanTrialAsync(int rpm1, int rpm2, int durationSeconds) =>
        RunFanTrialAsync("manual", rpm1, rpm2, durationSeconds);

    public async Task<FanOperationResult> RunFanTrialAsync(string mode, int rpm1, int rpm2, int durationSeconds)
    {
        if (IsBusy || currentIdentity is null)
            return new FanOperationResult(false, "Busy", Message: "系统忙碌或未识别");

        IsBusy = true;
        StatusText = $"正在启动限时试运行 ({mode}: {rpm1}/{rpm2} RPM, {durationSeconds}s)…";
        AddLog($"启动风扇受控试运行: 模式={mode}, Fan1={rpm1}, Fan2={rpm2}, 持续={durationSeconds}秒");
        using var cts = new CancellationTokenSource();
        activeFanTrialCts = cts;
        bool cancelled = false;
        BrokerResponse? resp = null;
        FanOperationResult result;
        try
        {
            var trial = new FanTrial(mode, rpm1, rpm2, durationSeconds);
            var req = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "fan-trial", currentIdentity, Trial: trial);
            resp = await brokerExecutor.ExecuteAsync(req, cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            FanStateText = "试运行异常";
            AddLog($"风扇试运行异常: {ex.Message}");
            StatusText = $"试运行失败: {ex.Message}";
            lastErrorTimestamp = DateTime.UtcNow;
            result = new FanOperationResult(false, "Failed", null, ex.Message);
        }

        try
        {
            if (cancelled)
            {
                FanStateText = "取消中 (恢复中)";
                StatusText = "风扇试运行已取消，正在等待底层恢复完成…";
                AddLog("已触发试运行取消，正在等待硬件控制锁释放…");

                using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var released = await brokerExecutor.WaitForRecoveryCompleteAsync(waitCts.Token);
                if (released)
                {
                    FanStateText = "已取消 (未确认)";
                    StatusText = "风扇试运行已取消，底层控制已释放 (未独立确认自动)";
                    AddLog("底层硬件锁已释放，解除控制锁定");
                }
                else
                {
                    FanStateText = "恢复未确认";
                    StatusText = "风扇试运行已取消，但未能确认恢复完成";
                    AddLog("恢复超时或未能确认底层控制锁释放");
                    lastErrorTimestamp = DateTime.UtcNow;
                }
                result = new FanOperationResult(false, "Cancelled", "UNCONFIRMED");
            }
            else if (resp != null)
            {
                var uiStatus = FanStatusMapper.MapTrial(resp, cancelled: false, mode);
                FanStateText = uiStatus.FanStateText;
                StatusText = uiStatus.StatusText;
                AddLog($"风扇试运行回执: {uiStatus.StatusText}");
                if (uiStatus.IsError)
                {
                    lastErrorTimestamp = DateTime.UtcNow;
                    result = new FanOperationResult(false, uiStatus.FanStateText, resp.Receipt?.Recovery, uiStatus.StatusText);
                }
                else
                {
                    result = new FanOperationResult(true, "Completed", resp.Receipt?.Recovery, uiStatus.StatusText);
                }
            }
            else
            {
                result = new FanOperationResult(false, "Failed", null, "无回执数据");
            }
        }
        finally
        {
            activeFanTrialCts = null;
            IsBusy = false;
            await RefreshTelemetryAsync();
        }
        return result;
    }
}

public record FanOperationResult(bool Success, string Status, string? Recovery = null, string? Message = null);
