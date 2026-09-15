using System.Globalization;
using System.Text.Json;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;

static class GpuTests
{
    private sealed class Probe : IReadOnlyProbe
    {
        public Task<JsonElement> QueryAsync(ProbeKind kind, CancellationToken ct) => Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            gpuTelemetry = new[] { new GpuSample(0, 48, 0, 6.65), new GpuSample(1, null, 50, 20) }
        }));
    }
    public static async Task RunAsync(DeviceIdentity identity, Action<bool, string> check)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var sample = NvidiaTelemetry.Parse("0, 48, 0, 6.65\r\n").Single();
            check(sample == new GpuSample(0, 48, 0, 6.65), "GPU parser uses invariant numbers and retains real zero utilization");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var missing = NvidiaTelemetry.Parse("0, N/A, NaN, -1").Single();
        check(missing.Temperature is null && missing.Utilization is null && missing.Power is null, "unavailable or invalid GPU metrics are not fabricated as zero");
        foreach (var invalid in new[] { "0,1,2", "0,1,2,3\n0,4,5,6", "-1,1,2,3", new string('x', 8193), string.Join('\n', Enumerable.Range(0, 9).Select(i => $"{i},1,2,3")) })
        {
            bool rejected = false;
            try
            {
                NvidiaTelemetry.Parse(invalid);
            }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "GPU parser rejects malformed duplicate or oversized output");
        }
        var snapshot = await new GenericProvider(new Probe()).ReadAsync(identity, default);
        check(snapshot.Readings.Count(r => r.Channel.StartsWith("nvidia-")) == 6, "GPU metrics retain separate device channels");
        check(snapshot.Capabilities.Count(c => c.Feature == Feature.GpuTemperature) == 1 && snapshot.Capabilities.All(c => !c.CanWrite), "multiple GPUs expose unique read-only capability entries");
    }
}
