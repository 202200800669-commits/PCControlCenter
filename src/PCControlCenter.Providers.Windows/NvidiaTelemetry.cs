using System.Diagnostics;
using System.Globalization;

namespace PCControlCenter.Providers.Windows;

public sealed record GpuSample(int Index, double? Temperature, double? Utilization, double? Power);
public static class NvidiaTelemetry
{
    public static IReadOnlyList<GpuSample> Parse(string csv)
    {
        if (csv.Length > 8192)
            throw new InvalidDataException("GPU_OUTPUT_TOO_LARGE");
        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 8)
            throw new InvalidDataException("TOO_MANY_GPUS");
        var seen = new HashSet<int>();
        var result = new List<GpuSample>();
        static double? Number(string text, double min, double max) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value) && value >= min && value <= max ? value : null;
        foreach (var line in lines)
        {
            var fields = line.Split(',');
            if (fields.Length != 4 || !int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index > 255 || !seen.Add(index))
                throw new InvalidDataException("INVALID_GPU_ROW");
            result.Add(new(index, Number(fields[1], -50, 150), Number(fields[2], 0, 100), Number(fields[3], 0, 2000)));
        }
        return result;
    }
    public static async Task<IReadOnlyList<GpuSample>> ReadAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return [];
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvidia-smi.exe");
        if (!File.Exists(path))
            return [];
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--query-gpu=index,temperature.gpu,utilization.gpu,power.draw");
        start.ArgumentList.Add("--format=csv,noheader,nounits");
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        Task<string>? output = null, errors = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!process.Start())
                return [];
            output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            await errors;
            return process.ExitCode == 0 ? Parse(text) : [];
        }
        catch (Exception) { ct.ThrowIfCancellationRequested(); return []; }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException) { }
            timeout.Cancel();
            if (output is not null && errors is not null)
                try
                {
                    await Task.WhenAll(output, errors);
                }
                catch (Exception) { }
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var result = new System.Text.StringBuilder();
        var buffer = new char[512];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (result.Length + count > 8192)
                throw new InvalidDataException("GPU_OUTPUT_TOO_LARGE");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
