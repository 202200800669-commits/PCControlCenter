using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PCControlCenter.Core;

namespace PCControlCenter.Providers.Windows;

public static class ModeSessionRunner
{
    public static async Task<ModeReceipt> RunAsync(ModeRequest request, CancellationToken stop)
    {
        if (!OperatingSystem.IsWindows() || !request.IsValid)
            return new("INVALID_REQUEST", request.Mode, Recovery: "NOT_NEEDED");

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

        using var process = new Process { StartInfo = start };
        stop.ThrowIfCancellationRequested();
        if (!process.Start())
            return new("WORKER_START_FAILED", request.Mode, Recovery: "NOT_NEEDED");

        using var registration = stop.Register(() => { try { process.StandardInput.Close(); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExitAsync();

        if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(25), stop)) != exited)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch { }
            if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(5))) != exited)
            {
                try
                {
                    process.Kill();
                }
                catch { }
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
}
