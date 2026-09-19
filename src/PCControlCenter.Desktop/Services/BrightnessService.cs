using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlCenter.Desktop.Services;

public record BrightnessResult(bool Success, string Status, int? ConfirmedValue, string? ErrorMessage = null);

public interface IBrightnessService
{
    Task<BrightnessResult> SetBrightnessAsync(int percent, CancellationToken ct = default);
    Task<int?> GetBrightnessAsync(CancellationToken ct = default);
}

public sealed class WindowsBrightnessService : IBrightnessService
{
    private readonly object gate = new();
    private int? pendingTarget;
    private Task<BrightnessResult>? currentRunningTask;

    public async Task<BrightnessResult> SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        percent = Math.Clamp(percent, 0, 100);

        lock (gate)
        {
            pendingTarget = percent;
            if (currentRunningTask == null || currentRunningTask.IsCompleted)
            {
                currentRunningTask = ProcessQueueAsync();
            }
        }

        return await currentRunningTask;
    }

    private async Task<BrightnessResult> ProcessQueueAsync()
    {
        BrightnessResult lastResult = new(false, "Unsupported", null);
        while (true)
        {
            int target;
            lock (gate)
            {
                if (!pendingTarget.HasValue)
                {
                    break;
                }
                target = pendingTarget.Value;
                pendingTarget = null;
            }

            lastResult = await ExecuteSetBrightnessAsync(target);
        }
        return lastResult;
    }

    private static async Task<BrightnessResult> ExecuteSetBrightnessAsync(int percent)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "Unsupported", null, "Not Windows");

        var psPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        var script = $$"""
        $ErrorActionPreference='Stop'
        try {
            $m = @(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods -ErrorAction SilentlyContinue)
            if ($null -eq $m -or $m.Count -eq 0) {
                @{ Status = 'Unsupported' } | ConvertTo-Json -Compress
                exit 0
            }
            $target = [byte]{{percent}}
            $inv = Invoke-CimMethod -InputObject $m[0] -MethodName WmiSetBrightness -Arguments @{ Timeout = 2; Brightness = $target } -ErrorAction Stop
            $cur = $null
            try {
                $b = @(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -ErrorAction SilentlyContinue)
                if ($b -and $b.Count -gt 0) { $cur = [int]$b[0].CurrentBrightness }
            } catch { }
            @{ Status = 'Success'; Confirmed = ($cur ?? $target) } | ConvertTo-Json -Compress
        } catch {
            @{ Status = 'Failed'; Message = $_.Exception.Message } | ConvertTo-Json -Compress
        }
        """;

        var start = new ProcessStartInfo(psPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(a);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                return new(false, "Failed", null, "Failed to start process");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var exitTask = process.WaitForExitAsync(cts.Token);

            try
            {
                await exitTask;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
                catch { }
                return new(false, "Timeout", null, "Brightness adjustment timed out");
            }

            var output = (await outputTask).Trim();
            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
            {
                var err = await errorTask;
                return new(false, "Failed", null, err);
            }

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var status = root.GetProperty("Status").GetString() ?? "Failed";
            if (status == "Success")
            {
                int confirmed = root.TryGetProperty("Confirmed", out var c) ? c.GetInt32() : percent;
                return new(true, "Success", confirmed);
            }
            else if (status == "Unsupported")
            {
                return new(false, "Unsupported", null, "Hardware or display driver does not support WmiMonitorBrightnessMethods");
            }
            else
            {
                string msg = root.TryGetProperty("Message", out var m) ? m.GetString() ?? "" : "";
                return new(false, "Failed", null, msg);
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }
            return new(false, "Failed", null, ex.Message);
        }
    }

    public async Task<int?> GetBrightnessAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return await Task.Run(() =>
            {
                return (int?)null;
            }, ct);
        }
        catch { return null; }
    }
}
