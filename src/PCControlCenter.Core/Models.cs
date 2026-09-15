namespace PCControlCenter.Core;

public enum SupportLevel
{
    Unsupported, ReadOnly, Experimental, Verified
}
public enum Feature
{
    DeviceInfo, Memory, Battery, Brightness, FanRpm, FanControl, FanTrial, PerformanceMode, CpuPower, Charging, Keyboard, GraphicsMode, GpuTemperature, GpuUtilization, GpuPower
}
public enum ResultCode
{
    Success, Unsupported, InvalidRequest, Cancelled, Timeout, Unavailable, UnknownOutcome
}
public sealed record DeviceIdentity(string Manufacturer, string Product, string Model, string Bios, string Platform);
public sealed record Capability(Feature Feature, SupportLevel Level, bool CanWrite, string Unit, double? Min = null, double? Max = null, string? Reason = null);
public sealed record Reading(Feature Feature, string Channel, double? Value, string Unit, DateTimeOffset At, string Status);
public sealed record Snapshot(string Provider, DeviceIdentity Device, IReadOnlyList<Capability> Capabilities, IReadOnlyList<Reading> Readings, IReadOnlyList<string> IssueCodes, SystemDetails? System = null);
public sealed record ControlRequest(Feature Feature, double Value);
public sealed record ControlResult(ResultCode Code, string Message);

public interface IHardwareProvider
{
    string Id
    {
        get;
    }
    bool Matches(DeviceIdentity device);
    Task<Snapshot> ReadAsync(DeviceIdentity device, CancellationToken ct);
    Task<ControlResult> ApplyAsync(DeviceIdentity device, ControlRequest request, CancellationToken ct);
}

public sealed class ProviderRegistry(IEnumerable<IHardwareProvider> providers, IHardwareProvider fallback)
{
    public IHardwareProvider Resolve(DeviceIdentity device)
    {
        var matches = providers.Where(p => p.Matches(device)).ToArray();
        // Ambiguous identities must not select a vendor control path.
        return matches.Length == 1 ? matches[0] : fallback;
    }
}

public sealed record DisplayDetails(string Name, string DriverVersion, string Source = "Unknown");
public sealed record SystemDetails(string OsVersion, string OsBuild, string CpuName, string BoardMaker, string BoardProduct, IReadOnlyList<DisplayDetails> Displays);
public static class PublicText
{
    public static string Clean(string? value) => new((value ?? "").Where(c => !char.IsControl(c)).Take(160).ToArray());
}
