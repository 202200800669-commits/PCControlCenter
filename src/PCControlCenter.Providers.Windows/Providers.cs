using PCControlCenter.Core;
namespace PCControlCenter.Providers.Windows;

public class GenericProvider(IReadOnlyProbe probe) : IHardwareProvider
{
    protected readonly IReadOnlyProbe Probe = probe;
    public virtual string Id => "windows.generic";
    public virtual bool Matches(DeviceIdentity device) => true;
    public virtual async Task<Snapshot> ReadAsync(DeviceIdentity device, CancellationToken ct)
    {
        var readings = new List<Reading>();
        var codes = new List<string>();
        SystemDetails? system = null;
        try
        {
            var data = await Probe.QueryAsync(ProbeKind.Generic, ct);
            string Field(System.Text.Json.JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? PublicText.Clean(v.GetString()) : "";
            var displays = new List<DisplayDetails>();
            if (data.TryGetProperty("displays", out var gpus) && gpus.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var gpu in gpus.EnumerateArray().Take(8))
                    if (gpu.ValueKind == System.Text.Json.JsonValueKind.Object)
                        displays.Add(new(Field(gpu, "name"), Field(gpu, "driverVersion"), Field(gpu, "source")));
            var ifaces = new List<string>();
            if (data.TryGetProperty("interfaces", out var ifaceArr) && ifaceArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in ifaceArr.EnumerateArray().Take(16))
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String && item.GetString() is { Length: > 0 } str)
                        ifaces.Add(PublicText.Clean(str));
            var powerSource = Field(data, "powerSource");
            var powerPlan = Field(data, "powerPlan");
            system = new(Field(data, "osVersion"), Field(data, "osBuild"), Field(data, "cpuName"), Field(data, "boardMaker"), Field(data, "boardProduct"), displays, ifaces, powerSource, powerPlan);
            foreach (var (key, feature) in new[] { ("memory", Feature.Memory), ("battery", Feature.Battery), ("brightness", Feature.Brightness) })
            {
                double? value = null;
                if (data.TryGetProperty(key, out var item) && item.ValueKind == System.Text.Json.JsonValueKind.Number && item.TryGetDouble(out var v) && double.IsFinite(v) && v >= 0 && v <= 100)
                    value = v;
                readings.Add(new(feature, key, value, "%", DateTimeOffset.UtcNow, value is null ? "unavailable" : "ok"));
            }
            if (!string.IsNullOrEmpty(powerSource))
            {
                var val = powerSource == "AC" ? 1d : 0d;
                readings.Add(new(Feature.PowerSource, "power-source", val, "state", DateTimeOffset.UtcNow, powerSource));
            }
            if (!string.IsNullOrEmpty(powerPlan))
            {
                readings.Add(new(Feature.PowerPlan, "power-plan", null, "plan", DateTimeOffset.UtcNow, powerPlan));
            }
            if (data.TryGetProperty("gpuTelemetry", out var telemetry) && telemetry.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var gpu in telemetry.EnumerateArray().Take(8))
                {
                    var index = gpu.GetProperty("Index").GetInt32();
                    if (index is < 0 or > 255)
                        continue;
                    foreach (var (key, feature, unit, min, max) in new[] {
                        ("Temperature", Feature.GpuTemperature, "°C", -50d, 150d),
                        ("Utilization", Feature.GpuUtilization, "%", 0d, 100d),
                        ("Power", Feature.GpuPower, "W", 0d, 2000d) })
                    {
                        double? value = null;
                        var item = gpu.GetProperty(key);
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Number && item.TryGetDouble(out var number) && double.IsFinite(number) && number >= min && number <= max)
                            value = number;
                        readings.Add(new(feature, "nvidia-" + index, value, unit, DateTimeOffset.UtcNow, value is null ? "unavailable" : "ok"));
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { codes.Add("GENERIC_PROBE_FAILED"); }
        var caps = new List<Capability> { new(Feature.DeviceInfo, SupportLevel.ReadOnly, false, "") };
        caps.AddRange(readings.Where(r => r.Value is not null).Select(r => new Capability(r.Feature, SupportLevel.ReadOnly, false, r.Unit)).DistinctBy(c => c.Feature));
        foreach (var f in Enum.GetValues<Feature>().Where(f => caps.All(c => c.Feature != f)))
            caps.Add(new(f, SupportLevel.Unsupported, false, "", Reason: "No verified adapter"));
        return new(Id, device, caps, readings, codes, system);
    }
    public Task<ControlResult> ApplyAsync(DeviceIdentity device, ControlRequest request, CancellationToken ct) =>
     Task.FromResult(new ControlResult(ResultCode.Unsupported, "Persistent writes are not available through the generic interface"));
}

public sealed class ThinkBookProvider(IReadOnlyProbe probe) : GenericProvider(probe)
{
    public override string Id => "lenovo.thinkbook.21r0";
    public override bool Matches(DeviceIdentity d) =>
     d.Platform == "Windows" && d.Manufacturer.Equals("LENOVO", StringComparison.OrdinalIgnoreCase) &&
     d.Product == "21R0" && d.Model == "ThinkBook 16p G6 IAX" && d.Bios == "R2CN57WW";
    public override async Task<Snapshot> ReadAsync(DeviceIdentity device, CancellationToken ct)
    {
        if (!Matches(device))
            throw new InvalidOperationException("IDENTITY_MISMATCH");
        var snapshot = await base.ReadAsync(device, ct);
        try
        {
            var modeData = await Probe.QueryAsync(ProbeKind.ThinkBookMode, ct);
            var mode = modeData.GetProperty("mode").GetInt32();
            if (mode is not (0 or 1 or 3 or 4))
                throw new IOException("UNKNOWN_MODE");
            snapshot = snapshot with
            {
                Readings = [.. snapshot.Readings, new(Feature.PerformanceMode, "lenovo-its-mode", mode, "mode", DateTimeOffset.UtcNow, "ok")],
                Capabilities = snapshot.Capabilities.Select(c => c.Feature == Feature.PerformanceMode ? new Capability(Feature.PerformanceMode, SupportLevel.ReadOnly, false, "mode") : c).ToArray()
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { snapshot = snapshot with { IssueCodes = [.. snapshot.IssueCodes, "THINKBOOK_MODE_UNAVAILABLE"] }; }

        try
        {
            var data = await Probe.QueryAsync(ProbeKind.ThinkBookFans, ct);
            var readings = snapshot.Readings.ToList();
            foreach (var key in new[] { "fan1", "fan2" })
            {
                var v = data.GetProperty(key).GetInt32();
                if (v < 0 || v > 10000)
                    throw new IOException("INVALID_READING");
                readings.Add(new(Feature.FanRpm, key, v, "RPM", DateTimeOffset.UtcNow, "ok"));
            }
            return snapshot with
            {
                Readings = readings,
                Capabilities = snapshot.Capabilities.Select(c => c.Feature switch { Feature.FanRpm => new Capability(Feature.FanRpm, SupportLevel.ReadOnly, false, "RPM"), Feature.FanTrial => new Capability(Feature.FanTrial, SupportLevel.Experimental, true, "RPM", 1500, 5500, "Explicit UAC; maximum 30 seconds; restores auto commands"), _ => c }).ToArray()
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (ProbeException e) { var code = e.Code switch { "ACCESS_DENIED" => "THINKBOOK_FAN_ACCESS_DENIED", "ELEVATION_CANCELLED" => "ELEVATION_CANCELLED", "BROKER_NOT_INSTALLED" => "BROKER_NOT_INSTALLED", _ => "BROKER_READ_UNAVAILABLE" }; return snapshot with { IssueCodes = [.. snapshot.IssueCodes, code] }; }
        catch (Exception) { return snapshot with { IssueCodes = [.. snapshot.IssueCodes, "THINKBOOK_FAN_READ_UNAVAILABLE"] }; }
    }
}

public static class AsusWmiProtocol
{
    public const string WmiNamespace = @"root\wmi";
    public const string WmiClassName = "AsusAtkWmi_WMNB";
    public const string MethodDsts = "DSTS";
    public const string MethodDevs = "DEVS";
    public const uint DeviceThermalPolicy = 0x00120075;
    public const uint DeviceBatteryHealth = 0x00120057;
    public const uint DeviceFanSpeed = 0x00110013;
    public const uint DeviceGpuMux = 0x00090020;
}

public sealed class AsusProvider(IReadOnlyProbe probe) : GenericProvider(probe)
{
    public override string Id => "asus.discovery";
    public override bool Matches(DeviceIdentity d) =>
        d.Manufacturer.Equals("ASUSTeK COMPUTER INC.", StringComparison.OrdinalIgnoreCase) ||
        d.Manufacturer.Equals("ASUS", StringComparison.OrdinalIgnoreCase);

    public override async Task<Snapshot> ReadAsync(DeviceIdentity device, CancellationToken ct)
    {
        var snapshot = await base.ReadAsync(device, ct);
        var issues = new List<string>(snapshot.IssueCodes);
        var hasWmi = snapshot.System?.DiscoveredInterfaces?.Contains("ASUS_WMI") == true;
        if (hasWmi)
            issues.Add("ASUS_WMI_ACTIVE");
        else
            issues.Add("ASUS_WMI_INTERFACE_UNAVAILABLE");

        var conceptualCaps = new List<Capability>(snapshot.Capabilities.Where(c => c.Feature is not (Feature.PerformanceMode or Feature.FanRpm or Feature.FanControl or Feature.GraphicsMode or Feature.Battery)));
        conceptualCaps.Add(new(Feature.Battery, SupportLevel.ReadOnly, false, "%", 60, 100, Reason: "ASUS Battery Health (0x00120057); awaiting GitHub user verification"));
        conceptualCaps.Add(new(Feature.PerformanceMode, SupportLevel.ReadOnly, false, "mode", Reason: "ASUS ACPI-WMI Thermal Policy (0x00120075); awaiting GitHub user verification"));
        conceptualCaps.Add(new(Feature.FanRpm, SupportLevel.ReadOnly, false, "RPM", Reason: "ASUS ACPI-WMI Fan Telemetry (0x00110013); awaiting GitHub user verification"));
        conceptualCaps.Add(new(Feature.FanControl, SupportLevel.Unsupported, false, "RPM", 1500, 5500, Reason: "ASUS Fan Write; locked pending community feedback"));
        conceptualCaps.Add(new(Feature.GraphicsMode, SupportLevel.Unsupported, false, "mode", Reason: "ASUS GPU MUX (0x00090020); locked pending community feedback"));

        return snapshot with
        {
            IssueCodes = issues,
            Capabilities = conceptualCaps
        };
    }
}

public static class MechrevoProtocol
{
    public const string WmiNamespace = @"root\wmi";
    public const string ClassFilterPattern = "Uniwill|Tongfang";
    public const string ServiceName = "UniwillService";
    public const string DriverName = "GCU.sys";
}

public sealed class MechrevoProvider(IReadOnlyProbe probe) : GenericProvider(probe)
{
    public override string Id => "mechrevo.discovery";
    public override bool Matches(DeviceIdentity d) =>
        d.Manufacturer.Equals("MECHREVO", StringComparison.OrdinalIgnoreCase);

    public override async Task<Snapshot> ReadAsync(DeviceIdentity device, CancellationToken ct)
    {
        var snapshot = await base.ReadAsync(device, ct);
        var issues = new List<string>(snapshot.IssueCodes);
        var hasWmi = snapshot.System?.DiscoveredInterfaces?.Contains("UNIWILL_WMI") == true;
        if (hasWmi)
            issues.Add("MECHREVO_WMI_ACTIVE");
        else
            issues.Add("MECHREVO_INTERFACE_UNAVAILABLE");

        var conceptualCaps = new List<Capability>(snapshot.Capabilities.Where(c => c.Feature is not (Feature.PerformanceMode or Feature.FanRpm or Feature.Charging)));
        conceptualCaps.Add(new(Feature.PerformanceMode, SupportLevel.ReadOnly, false, "mode", Reason: "Mechrevo/Uniwill Mode Profile; awaiting GitHub user verification"));
        conceptualCaps.Add(new(Feature.FanRpm, SupportLevel.ReadOnly, false, "RPM", Reason: "Mechrevo/Uniwill EC Fan Telemetry; awaiting GitHub user verification"));
        conceptualCaps.Add(new(Feature.Charging, SupportLevel.ReadOnly, false, "%", Reason: "Mechrevo Battery Limit; awaiting GitHub user verification"));

        return snapshot with
        {
            IssueCodes = issues,
            Capabilities = conceptualCaps
        };
    }
}

// Brand discovery is preserved for backward compatibility
public class BrandDiscoveryProvider(IReadOnlyProbe probe, string brand) : GenericProvider(probe)
{
    public override string Id => brand + ".discovery";
    public override bool Matches(DeviceIdentity d) => brand switch
    {
        "asus" => d.Manufacturer.Equals("ASUSTeK COMPUTER INC.", StringComparison.OrdinalIgnoreCase) || d.Manufacturer.Equals("ASUS", StringComparison.OrdinalIgnoreCase),
        "mechrevo" => d.Manufacturer.Equals("MECHREVO", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}

public static class Providers
{
    public static ProviderRegistry Create(IReadOnlyProbe probe) => new([
     new ThinkBookProvider(probe),
     new AsusProvider(probe),
     new MechrevoProvider(probe)], new GenericProvider(probe));
}
