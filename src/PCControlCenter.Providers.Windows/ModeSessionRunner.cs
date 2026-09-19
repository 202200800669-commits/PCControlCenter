using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCControlCenter.Core;

namespace PCControlCenter.Providers.Windows;

public static class ModeSessionRunner
{
    public static TimeSpan TargetPhaseTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public static TimeSpan RecoveryWaitTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public static Func<ProcessStartInfo, Process?>? ProcessLauncher
    {
        get; set;
    }

    public static async Task<ModeReceipt> RunAsync(ModeRequest request, CancellationToken stop, string requestId = "")
    {
        if (!OperatingSystem.IsWindows() || !request.IsValid)
            return new("INVALID_REQUEST", request.Mode, Recovery: "NOT_NEEDED");

        if (RecoveryProcessTracker.HasInFlightRecovery)
            return new("BUSY", request.Mode, Recovery: "UNCONFIRMED");

        using var resource = typeof(ModeSessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.ModeSession.ps1")
            ?? throw new IOException("RESOURCE_MISSING");
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync();

        var prefix = string.Create(CultureInfo.InvariantCulture, $"$targetMode={request.Mode};");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(prefix + script)) })
            start.ArgumentList.Add(arg);

        var process = ProcessLauncher != null ? ProcessLauncher(start) : new Process { StartInfo = start };
        if (process == null)
            return new("WORKER_START_FAILED", request.Mode, Recovery: "NOT_NEEDED");

        stop.ThrowIfCancellationRequested();
        if (!process.Start())
        {
            try
            {
                process.Dispose();
            }
            catch { }
            return new("WORKER_START_FAILED", request.Mode, Recovery: "NOT_NEEDED");
        }

        bool transferred = false;
        try
        {
            using var registration = stop.Register(() => { try { process.StandardInput.Close(); } catch { } });
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExitAsync();

            // 阶段 1：目标操作阶段（由 stop 令牌与 TargetPhaseTimeout 控制）
            if (await Task.WhenAny(exited, Task.Delay(TargetPhaseTimeout, stop)) != exited)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch { }

                // 阶段 2：恢复执行阶段（脱离已取消的 stop 令牌，给予子进程充分等待时限）
                if (await Task.WhenAny(exited, Task.Delay(RecoveryWaitTimeout)) != exited)
                {
                    // 超时但底层工作者仍在执行恢复，所有权真正移交给后台跟踪器，禁止调用方 Dispose
                    RecoveryProcessTracker.Track(process, requestId, @"Global\PCControlCenter.ThinkBookModeSession");
                    transferred = true;
                    return new("WORKER_TIMEOUT", request.Mode, Recovery: "UNCONFIRMED");
                }
            }

            var json = await output;
            await errors;
            if (process.ExitCode != 0 || json.Length > 32768)
                return new("WORKER_FAILED", request.Mode, Recovery: "UNCONFIRMED");

            try
            {
                return JsonSerializer.Deserialize<ModeReceipt>(json) ?? new("INVALID_RECEIPT", request.Mode, Recovery: "UNCONFIRMED");
            }
            catch (JsonException)
            {
                return new("INVALID_RECEIPT", request.Mode, Recovery: "UNCONFIRMED");
            }
        }
        finally
        {
            if (!transferred && process != null)
            {
                try
                {
                    process.Dispose();
                }
                catch { }
            }
        }
    }
}
