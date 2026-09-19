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

public static class FanSessionRunner
{
    public static TimeSpan TargetPhaseTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public static TimeSpan RecoveryWaitTimeout { get; set; } = TimeSpan.FromSeconds(80);
    public static Func<ProcessStartInfo, Process?>? ProcessLauncher
    {
        get; set;
    }

    public static async Task<FanReceipt> RunAsync(FanTrial trial, CancellationToken stop, string requestId = "")
    {
        if (!OperatingSystem.IsWindows() || !trial.IsValid)
            return new("INVALID_REQUEST", "NOT_NEEDED");

        if (RecoveryProcessTracker.HasInFlightRecovery)
            return new("BUSY", "UNCONFIRMED");

        using var resource = typeof(FanSessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.FanSession.ps1")
            ?? throw new IOException("RESOURCE_MISSING");
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync();

        var prefix = string.Create(CultureInfo.InvariantCulture, $"$mode='{trial.Mode}';$rpm1={trial.Rpm1};$rpm2={trial.Rpm2};$seconds={trial.Seconds};");
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
            return new("WORKER_START_FAILED", "NOT_NEEDED");

        stop.ThrowIfCancellationRequested();
        if (!process.Start())
        {
            try
            {
                process.Dispose();
            }
            catch { }
            return new("WORKER_START_FAILED", "NOT_NEEDED");
        }

        bool transferred = false;
        try
        {
            using var registration = stop.Register(() => { try { process.StandardInput.Close(); } catch { } });
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExitAsync();

            if (await Task.WhenAny(exited, Task.Delay(RecoveryWaitTimeout)) != exited)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch { }

                // 超时移交给后台跟踪器，所有权移交，调用方 finally 不 Dispose
                RecoveryProcessTracker.Track(process, requestId, @"Global\PCControlCenter.ThinkBookFanSession");
                transferred = true;
                return new("WORKER_TIMEOUT", "UNCONFIRMED");
            }

            var json = await output;
            await errors;
            if (process.ExitCode != 0 || json.Length > 32768)
                return new("WORKER_FAILED", "UNCONFIRMED");

            try
            {
                return JsonSerializer.Deserialize<FanReceipt>(json) ?? new("INVALID_RECEIPT", "UNCONFIRMED");
            }
            catch (JsonException)
            {
                return new("INVALID_RECEIPT", "UNCONFIRMED");
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
