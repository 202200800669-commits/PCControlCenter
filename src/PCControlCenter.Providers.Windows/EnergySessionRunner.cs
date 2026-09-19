using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PCControlCenter.Core;

namespace PCControlCenter.Providers.Windows;

public static class EnergySessionRunner
{
    public static async Task<EnergyReceipt> RunAsync(EnergyRequest request, CancellationToken stop)
    {
        if (!OperatingSystem.IsWindows() || !request.IsValid)
            return new("INVALID_REQUEST", request.Kind, request.Value, Recovery: "NOT_NEEDED");

        using var resource = typeof(EnergySessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.EnergySession.ps1")
            ?? throw new IOException("RESOURCE_MISSING");
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync();

        var prefix = string.Create(CultureInfo.InvariantCulture, $"$kind='{request.Kind}';$value={request.Value};");
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

        using var process = new Process { StartInfo = start };
        stop.ThrowIfCancellationRequested();
        if (!process.Start())
            return new("WORKER_START_FAILED", request.Kind, request.Value, Recovery: "NOT_NEEDED");

        using var registration = stop.Register(() => { try { process.StandardInput.Close(); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExitAsync();

        // 阶段 1：目标操作阶段（由 stop 令牌与 20 秒目标时限控制）
        if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(20), stop)) != exited)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch { }

            // 阶段 2：恢复执行阶段（不使用已取消的 stop 令牌，给予子进程充分的恢复完成硬时限）
            if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(60))) != exited)
            {
                try
                {
                    process.Kill(true);
                }
                catch { }
                return new("WORKER_TIMEOUT", request.Kind, request.Value, Recovery: "UNCONFIRMED");
            }
        }

        var json = await output;
        await errors;
        if (process.ExitCode != 0 || json.Length > 32768)
            return new("WORKER_FAILED", request.Kind, request.Value, Recovery: "UNCONFIRMED");

        try
        {
            return JsonSerializer.Deserialize<EnergyReceipt>(json) ?? new("INVALID_RECEIPT", request.Kind, request.Value, Recovery: "UNCONFIRMED");
        }
        catch (JsonException)
        {
            return new("INVALID_RECEIPT", request.Kind, request.Value, Recovery: "UNCONFIRMED");
        }
    }
}
