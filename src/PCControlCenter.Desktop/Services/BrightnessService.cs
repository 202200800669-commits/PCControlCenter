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

public class WindowsBrightnessService : IBrightnessService
{
    private sealed record PendingRequest(int Percent, CancellationToken Cancellation, TaskCompletionSource<BrightnessResult> Completion);

    private readonly object gate = new();
    private PendingRequest? pendingRequest;
    private bool isProcessing;
    private readonly Func<int, CancellationToken, Task<BrightnessResult>> executor;
    private readonly Func<CancellationToken, Task<int?>> getter;

    public WindowsBrightnessService() : this(ExecuteSetBrightnessAsync, ExecuteGetBrightnessAsync)
    {
    }

    public WindowsBrightnessService(
        Func<int, CancellationToken, Task<BrightnessResult>> executor,
        Func<CancellationToken, Task<int?>>? getter = null)
    {
        this.executor = executor;
        this.getter = getter ?? ExecuteGetBrightnessAsync;
    }

    public static string GetScript()
    {
        using var stream = typeof(WindowsBrightnessService).Assembly.GetManifestResourceStream("PCControlCenter.Desktop.Services.BrightnessSession.ps1");
        if (stream != null)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        var directPath = Path.Combine(AppContext.BaseDirectory, "Services", "BrightnessSession.ps1");
        if (File.Exists(directPath))
            return File.ReadAllText(directPath, Encoding.UTF8);

        throw new FileNotFoundException("BrightnessSession.ps1 resource not found");
    }

    public Task<BrightnessResult> SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Task.FromResult(new BrightnessResult(false, "Cancelled", null, "Operation cancelled by caller"));

        percent = Math.Clamp(percent, 0, 100);

        var tcs = new TaskCompletionSource<BrightnessResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (gate)
        {
            if (pendingRequest != null)
            {
                pendingRequest.Completion.TrySetResult(new BrightnessResult(false, "Cancelled", null, "Superseded before write"));
            }

            pendingRequest = new PendingRequest(percent, ct, tcs);

            if (!isProcessing)
            {
                isProcessing = true;
                _ = Task.Run(ProcessQueueAsync);
            }
        }

        if (ct.CanBeCanceled)
        {
            var reg = ct.Register(() =>
            {
                lock (gate)
                {
                    if (pendingRequest?.Completion == tcs)
                    {
                        pendingRequest = null;
                        tcs.TrySetResult(new BrightnessResult(false, "Cancelled", null, "Operation cancelled by caller"));
                    }
                }
            });
            _ = tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
        }

        return tcs.Task;
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            PendingRequest? current;
            lock (gate)
            {
                current = pendingRequest;
                pendingRequest = null;
                if (current == null)
                {
                    isProcessing = false;
                    break;
                }
            }

            if (current.Cancellation.IsCancellationRequested)
            {
                current.Completion.TrySetResult(new BrightnessResult(false, "Cancelled", null, "Operation cancelled by caller"));
                continue;
            }

            try
            {
                var result = await executor(current.Percent, current.Cancellation);
                current.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                current.Completion.TrySetResult(new BrightnessResult(false, "Cancelled", null, "Operation cancelled by caller"));
            }
            catch (Exception ex)
            {
                current.Completion.TrySetResult(new BrightnessResult(false, "Failed", null, ex.Message));
            }
        }
    }

    public static async Task<BrightnessResult> ExecuteSetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "Unsupported", null, "Not Windows");

        string script;
        try
        {
            script = GetScript();
        }
        catch (Exception ex)
        {
            return new(false, "Failed", null, "Script load failed: " + ex.Message);
        }

        var prefix = $"$percent = {percent};\n";
        var fullScript = prefix + script;

        var psPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(psPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(fullScript)) })
            start.ArgumentList.Add(a);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                return new(false, "Failed", null, "Failed to start powershell process");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
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

                if (ct.IsCancellationRequested)
                    return new(false, "Cancelled", null, "Brightness adjustment cancelled");
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
                int? confirmed = root.TryGetProperty("Confirmed", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                return new(true, "Success", confirmed);
            }
            else if (status == "SuccessUnconfirmed")
            {
                return new(false, "SuccessUnconfirmed", null, "Brightness command executed but readback confirmation is unavailable");
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

    public Task<int?> GetBrightnessAsync(CancellationToken ct = default) => getter(ct);

    public static async Task<int?> ExecuteGetBrightnessAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var psPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        var script = "$ErrorActionPreference='SilentlyContinue'; $b = @(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness | Where-Object { $_.Active }); if ($b.Count -gt 0 -and $null -ne $b[0].CurrentBrightness) { [int]$b[0].CurrentBrightness }";

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
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            var exitTask = process.WaitForExitAsync(cts.Token);

            var outputTask = process.StandardOutput.ReadToEndAsync();
            await exitTask;

            var output = (await outputTask).Trim();
            if (int.TryParse(output, out var val) && val is >= 0 and <= 100)
                return val;
            return null;
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }
            return null;
        }
    }
}
