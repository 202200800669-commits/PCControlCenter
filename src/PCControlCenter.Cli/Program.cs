using PCControlCenter.Ipc;
using System.Text;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;
Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0 || args is ["--help"])
{
    Console.WriteLine("PC Control Center 0.1.0-alpha.13\nprobe                       只读设备检测与诊断预览（加 --elevated 可请求风扇读取权限）\nexport <new-file.zip>        导出本地诊断包（不上传、不覆盖）\nimport-preferences <old.json> <new.json>  导入非硬件偏好\nfans manual <rpm1> <rpm2> <seconds>  限时手动调速\nfans full <seconds>                 限时全速\nfans auto                           恢复自动\nmode <0|1|3>                        切换联想性能模式（0=均衡, 1=野兽, 3=安静）\nenergy <charge|key|night> <value>   设置能源/外设（charge: 0=普通/1=养护/2=快充; key: 0=关/1=低/2=高/3=自动; night: 0=关/1=开）\ninspect-report <feedback.zip>         检查用户反馈包\nwatch <1-30>                        连续只读监控，Ctrl+C 结束");
    return 0;
}
if (args is ["inspect-report", var reportFile])
{
    try
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(Feedback.Inspect(reportFile), Diagnostics.Json));
        return 0;
    }
    catch (Exception) { Console.Error.WriteLine("诊断包格式不受支持或内容不完整；没有解压或执行任何文件。"); return 11; }
}
if (args is ["import-preferences", var legacy, var destination])
{
    try
    {
        PreferenceStore.ImportLegacy(legacy, destination);
        Console.WriteLine("已导入托盘与刷新间隔偏好；硬件设置未迁移。");
        return 0;
    }
    catch (Exception) { Console.Error.WriteLine("配置导入失败：检查文件格式、版本、范围及目标是否已存在。"); return 7; }
}
int? watchSeconds = null;
if (args is ["watch", var watchInterval] && int.TryParse(watchInterval, out var intervalSeconds) && intervalSeconds is >= 1 and <= 30) watchSeconds = intervalSeconds;
FanTrial? trial = null;
if (args is ["fans", "auto"])
    trial = new("auto", 0, 0, 0);
else if (args is ["fans", "full", var duration] && int.TryParse(duration, out var fullSeconds))
    trial = new("full", 0, 0, fullSeconds);
else if (args is ["fans", "manual", var one, var two, var duration2] && int.TryParse(one, out var rpm1) && int.TryParse(two, out var rpm2) && int.TryParse(duration2, out var manualSeconds))
    trial = new("manual", rpm1, rpm2, manualSeconds);
ModeRequest? modeRequest = null;
if (args is ["mode", var modeStr] && int.TryParse(modeStr, out var m) && m is 0 or 1 or 3)
    modeRequest = new(m);
if (args is ["mode", _] && modeRequest is null) { Console.Error.WriteLine("参数无效：性能模式仅支持 0（均衡）、1（野兽/高性能）、3（安静/节能）。"); return 2; }
EnergyRequest? energyRequest = null;
if (args is ["energy", var kindStr, var valStr] && int.TryParse(valStr, out var ev))
{
    var candidate = new EnergyRequest(kindStr, ev);
    if (candidate.IsValid) energyRequest = candidate;
}
if (args is ["energy", ..] && energyRequest is null) { Console.Error.WriteLine("参数无效：energy charge <0-2>（0=普通, 1=养护, 2=快充）/ energy key <0-3>（0=关, 1=低, 2=高, 3=自动）/ energy night <0-1>。"); return 2; }
if (trial is not null && !trial.IsValid) { Console.Error.WriteLine("参数无效：转速 1500–5500 RPM，时限 5–30 秒。"); return 2; }
if (trial is null && modeRequest is null && energyRequest is null && watchSeconds is null && !(args is ["probe"] || args is ["export", _] || args is ["probe", "--elevated"] || args is ["export", _, "--elevated"])) { Console.Error.WriteLine("参数无效，使用 --help。"); return 2; }
if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("当前探测器仅支持 Windows。"); return 3; }
using var cancellation = new CancellationTokenSource(watchSeconds is null ? TimeSpan.FromSeconds(110) : Timeout.InfiniteTimeSpan);
Console.CancelKeyPress += (s, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    IReadOnlyProbe probe = new WindowsProbe();
    var device = await WindowsProbe.IdentifyAsync(probe, cancellation.Token);
    if (trial is not null)
    {
        if (!new ThinkBookProvider(probe).Matches(device))
        {
            Console.Error.WriteLine("当前设备尚无经过验证的控制接口。");
            return 8;
        }
        Console.Error.WriteLine("即将请求管理员授权。试运行到期或连接断开后发送恢复自动散热命令。");
        var response = await BrokerClient.ExecuteAsync(new(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "fan-trial", device, trial), cancellation.Token);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response, Diagnostics.Json));
        return response.Code == "OK" && response.Receipt is { Code: "COMPLETED", Recovery: "AUTO_COMMANDS_SENT_OVERRIDE_OFF" } ? 0 : 9;
    }
    if (modeRequest is not null)
    {
        if (!new ThinkBookProvider(probe).Matches(device))
        {
            Console.Error.WriteLine("当前设备尚无经过验证的性能模式控制接口。");
            return 8;
        }
        Console.Error.WriteLine($"即将请求管理员授权。将性能模式切换为 {modeRequest.Mode}（0=均衡, 1=野兽, 3=安静）并回读确认。");
        var response = await BrokerClient.ExecuteAsync(new(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "set-mode", device, Mode: modeRequest), cancellation.Token);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response, Diagnostics.Json));
        return response.Code == "OK" && response.ModeReceipt is { Code: "COMPLETED" } ? 0 : 9;
    }
    if (energyRequest is not null)
    {
        if (!new ThinkBookProvider(probe).Matches(device))
        {
            Console.Error.WriteLine("当前设备尚无经过验证的能源控制接口。");
            return 8;
        }
        Console.Error.WriteLine($"即将请求管理员授权。将 {energyRequest.Kind} 设置为 {energyRequest.Value} 并回读确认。");
        var response = await BrokerClient.ExecuteAsync(new(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "set-energy", device, Energy: energyRequest), cancellation.Token);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response, Diagnostics.Json));
        return response.Code == "OK" && response.EnergyReceipt is { Code: "COMPLETED" } ? 0 : 9;
    }
    if (args.Contains("--elevated") && new ThinkBookProvider(probe).Matches(device))
    {
        Console.Error.WriteLine("即将请求 Windows 管理员授权，仅用于本次风扇读取；完成后代理自动退出。");
        probe = new ElevatedProbe(probe, device);
    }
    var provider = Providers.Create(probe).Resolve(device);
    if (watchSeconds is int seconds)
    {
        var compact = new System.Text.Json.JsonSerializerOptions(Diagnostics.Json) { WriteIndented = false };
        await foreach (var sample in new MonitorSession(provider, device, TimeSpan.FromSeconds(seconds)).WatchAsync(cancellation.Token))
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(Diagnostics.Report(sample), compact));
        return 0;
    }
    var snapshot = await provider.ReadAsync(device, cancellation.Token);
    if (args[0] == "probe")
        Console.WriteLine(Diagnostics.ToJson(snapshot));
    else
    {
        Diagnostics.Export(snapshot, Path.GetFullPath(args[1]));
        Console.WriteLine(snapshot.IssueCodes.Count == 0 ? "诊断已导出。" : "诊断已导出，未完成项：" + string.Join(", ", snapshot.IssueCodes));
    }
    return 0;
}
catch (OperationCanceledException) { if (watchSeconds is not null) return 0; Console.Error.WriteLine("请求取消或超时；若试运行已开始，代理会尝试恢复自动散热，结果需重新核对。"); return 4; }
catch (ProbeException e) { Console.Error.WriteLine("代理错误码：" + e.Code); return 10; }
catch (IOException) { Console.Error.WriteLine("探测或文件操作失败；请检查接口可用性、输出目录和是否存在同名文件。"); return 5; }
catch (Exception) { Console.Error.WriteLine("检测未完成；没有导出原始异常或个人路径。"); return 6; }
