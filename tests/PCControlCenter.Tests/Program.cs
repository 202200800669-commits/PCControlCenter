using PCControlCenter.Ipc;
using System.Buffers.Binary;
using System.IO.Pipes;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;
using System.Text.Json;
using System.IO.Compression;

if (args.Length == 4 && args[0] == "--recovery-test-host")
{
    await RecoveryLifecycleTests.RunHostAsync(args);
    return;
}

var device = new DeviceIdentity("LENOVO", "21R0", "ThinkBook 16p G6 IAX", "R2CN57WW", "Windows");
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
var probe = new FakeProbe();
var registry = Providers.Create(probe);
Check(registry.Resolve(device).Id == "lenovo.thinkbook.21r0", "exact reference identity");
foreach (var changed in new[] { device with { Bios = "other" }, device with { Manufacturer = "ASUS" }, device with { Product = "21R1" }, device with { Model = "ThinkBook 16p" }, device with { Platform = "Linux" } })
    Check(registry.Resolve(changed).Id != "lenovo.thinkbook.21r0", "changed identity excludes vendor probe");
Check(registry.Resolve(device with { Manufacturer = "ASUSTeK COMPUTER INC." }).Id == "asus.discovery", "ASUS discovery only");
Check(registry.Resolve(device with { Manufacturer = "MECHREVO" }).Id == "mechrevo.discovery", "MECHREVO discovery only");
Check(registry.Resolve(device with { Manufacturer = "Unknown" }).Id == "windows.generic", "unknown fallback");
var ambiguous = new ProviderRegistry([new ThinkBookProvider(probe), new ThinkBookProvider(probe)], new GenericProvider(probe));
Check(ambiguous.Resolve(device).Id == "windows.generic", "ambiguous matching fails closed");
var realProvider = registry.Resolve(device);
var snap = await realProvider.ReadAsync(device, default);
Check(snap.Readings.Count(r => r.Feature == Feature.FanRpm) == 2, "fan readings mapped");
Check(snap.Readings.Single(r => r.Feature == Feature.PerformanceMode).Value == 0 && snap.Capabilities.Single(c => c.Feature == Feature.PerformanceMode).Level == SupportLevel.ReadOnly, "performance mode read is separate from control capability");
Check(snap.Capabilities.Any(c => c.Feature == Feature.FanTrial && c.Level == SupportLevel.Experimental && c.CanWrite), "bounded fan trial explicitly marked experimental");
Check(snap.Capabilities.Where(c => c.Feature != Feature.FanTrial).All(c => !c.CanWrite), "monitoring providers advertise no persistent writes");
Check((await new Controller(realProvider, device).ApplyAsync(new(Feature.FanControl, 3000))).Code == ResultCode.Unsupported, "generic controller rejects persistent writes");
var asusDev = device with { Manufacturer = "ASUSTeK COMPUTER INC." };
var asusProvider = registry.Resolve(asusDev);
var asusSnap = await asusProvider.ReadAsync(asusDev, default);
Check(asusSnap.Capabilities.Any(c => c.Feature == Feature.PerformanceMode && c.Level == SupportLevel.ReadOnly && !c.CanWrite), "ASUS conceptual performance mode capability");
Check(asusSnap.Capabilities.Any(c => c.Feature == Feature.Battery && c.Min == 60 && c.Max == 100), "ASUS conceptual battery charging capability");
Check((await asusProvider.ApplyAsync(asusDev, new(Feature.PerformanceMode, 1), default)).Code == ResultCode.Unsupported, "ASUS writes locked pending feedback");
Check(asusSnap.IssueCodes.Contains("ASUS_WMI_INTERFACE_UNAVAILABLE"), "ASUS WMI missing reports diagnostic code");

var mechrevoDev = device with { Manufacturer = "MECHREVO" };
var mechrevoProvider = registry.Resolve(mechrevoDev);
var mechrevoSnap = await mechrevoProvider.ReadAsync(mechrevoDev, default);
Check(mechrevoSnap.Capabilities.Any(c => c.Feature == Feature.PerformanceMode && c.Level == SupportLevel.ReadOnly && !c.CanWrite), "Mechrevo conceptual performance mode capability");
Check((await mechrevoProvider.ApplyAsync(mechrevoDev, new(Feature.PerformanceMode, 1), default)).Code == ResultCode.Unsupported, "Mechrevo writes locked pending feedback");
Check(mechrevoSnap.IssueCodes.Contains("MECHREVO_INTERFACE_UNAVAILABLE"), "Mechrevo interface missing reports diagnostic code");

var issueMd = Diagnostics.FormatGitHubIssueMarkdown(snap);
Check(issueMd.Contains("### 硬件环境与固件信息") && issueMd.Contains("LENOVO") && issueMd.Contains("21R0"), "GitHub issue markdown contains identity and survey");
probe.FailFans = true;
var degraded = await realProvider.ReadAsync(device, default);
Check(degraded.IssueCodes.Contains("THINKBOOK_FAN_READ_UNAVAILABLE") && degraded.Readings.All(r => r.Feature != Feature.FanRpm), "failed fan read has no fabricated values");
probe.FailFans = false;
probe.BadFans = true;
degraded = await realProvider.ReadAsync(device, default);
Check(degraded.Readings.All(r => r.Feature != Feature.FanRpm), "invalid fan reading discarded");
probe.BadFans = false;
probe.Denied = true;
degraded = await realProvider.ReadAsync(device, default);
Check(degraded.IssueCodes.Contains("THINKBOOK_FAN_ACCESS_DENIED"), "access denial has dedicated diagnostic code");
probe.Denied = false;
probe.BadGeneric = true;
degraded = await new GenericProvider(probe).ReadAsync(device, default);
Check(degraded.Readings.All(r => r.Value is null), "invalid generic percentages are unavailable");
var fake = new FakeWriter(device);
var controller = new Controller(fake, device);
Check((await controller.ApplyAsync(new(Feature.FanControl, double.NaN))).Code == ResultCode.InvalidRequest && fake.Writes == 0, "NaN rejected before write");
Check((await controller.ApplyAsync(new(Feature.FanControl, 1499))).Code == ResultCode.InvalidRequest && fake.Writes == 0, "out of range rejected");
fake.Max = double.NaN;
Check((await controller.ApplyAsync(new(Feature.FanControl, 3000))).Code == ResultCode.InvalidRequest && fake.Writes == 0, "invalid capability bounds rejected");
fake.Max = 5500;
fake.Level = SupportLevel.Experimental;
Check((await controller.ApplyAsync(new(Feature.FanControl, 3000))).Code == ResultCode.Unsupported && fake.Writes == 0, "experimental writes locked");
fake.Level = SupportLevel.Verified;
fake.ChangeDevice = true;
Check((await controller.ApplyAsync(new(Feature.FanControl, 3000))).Code == ResultCode.Unavailable && fake.Writes == 0, "identity change rejected");
fake.ChangeDevice = false;
await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => controller.ApplyAsync(new(Feature.FanControl, 3000))));
Check(fake.Writes == 6 && fake.MaxConcurrent == 1, "concurrent commands serialized");
using (var cts = new CancellationTokenSource()) { cts.Cancel(); Check((await controller.ApplyAsync(new(Feature.FanControl, 3000), cts.Token)).Code == ResultCode.Cancelled && fake.Writes == 6, "cancelled queue never writes"); }
fake.CancelDuringWrite = true;
Check((await controller.ApplyAsync(new(Feature.FanControl, 3000))).Code == ResultCode.UnknownOutcome, "interrupted write is not falsely reported cancelled safely");
var privateSnapshot = snap with { IssueCodes = ["OK_CODE", @"C:\Users\PrivateName\secret"] };
var json = Diagnostics.ToJson(privateSnapshot);
Check(!json.Contains("PrivateName") && json.Contains("OK_CODE"), "diagnostics excludes raw error paths");
var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
try
{
    Diagnostics.Export(privateSnapshot, file);
    using (var zip = ZipFile.OpenRead(file))
    {
        Check(zip.Entries.Count == 1 && zip.Entries[0].FullName == "diagnostics.json", "minimal diagnostic archive");
    }
    var old = File.ReadAllBytes(file);
    bool rejected = false;
    try
    {
        Diagnostics.Export(privateSnapshot, file);
    }
    catch (IOException) { rejected = true; }
    Check(rejected && old.SequenceEqual(File.ReadAllBytes(file)), "existing diagnostics not overwritten");
}
finally { File.Delete(file); }
var configDir = Path.Combine(Path.GetTempPath(), "pc-control-test-" + Guid.NewGuid());
Directory.CreateDirectory(configDir);
try
{
    var source = Path.Combine(configDir, "old.json");
    var target = Path.Combine(configDir, "new.json");
    var original = """{"Tray":false,"Interval":5,"Profiles":[{"Rpm1":5500,"Mode":3}],"Accent":"private"}""";
    File.WriteAllText(source, original);
    PreferenceStore.ImportLegacy(source, target);
    var pref = PreferenceStore.Load(target);
    Check(pref == new Preferences(1, false, 5), "legacy non-hardware preferences imported");
    Check(!File.ReadAllText(target).Contains("Rpm") && !File.ReadAllText(target).Contains("Accent") && File.ReadAllText(source) == original, "migration preserves source and excludes hardware and design");
    var oldSettings = File.ReadAllText(target);
    bool rejected = false;
    try
    {
        PreferenceStore.ImportLegacy(source, target);
    }
    catch (IOException) { rejected = true; }
    Check(rejected && File.ReadAllText(target) == oldSettings, "migration does not overwrite existing configuration");
    rejected = false;
    try
    {
        PreferenceStore.Create(Path.Combine(configDir, "invalid.json"), new Preferences(2));
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && !File.Exists(Path.Combine(configDir, "invalid.json")), "unknown schema rejected before saving");
    rejected = false;
    try
    {
        PreferenceStore.Create(Path.Combine(configDir, "range.json"), new Preferences(1, true, 0));
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "invalid polling interval rejected");
    Check(!Directory.EnumerateFiles(configDir, "*.tmp").Any(), "failed migration leaves no temporary file");
    var before = PreferenceStore.Read(target);
    var update = PreferenceStore.Update(target, before.Revision, new Preferences(1, true, 7));
    Check(update.State.Value.PollIntervalSeconds == 7 && PreferenceStore.Load(update.BackupPath) == before.Value, "atomic preference update preserves previous configuration");
    rejected = false;
    try
    {
        PreferenceStore.Update(target, before.Revision, new Preferences(1, false, 8));
    }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected && PreferenceStore.Read(target) == update.State, "stale preference writer cannot overwrite newer changes");
    var restored = PreferenceStore.Restore(target, update.State.Revision, update.BackupPath);
    Check(restored.State.Value == before.Value && PreferenceStore.Load(restored.BackupPath) == update.State.Value, "explicit restore retains displaced preferences");
    using (var gate = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    {
        rejected = false;
        try
        {
            PreferenceStore.Update(target, restored.State.Revision, new Preferences());
        }
        catch (IOException) { rejected = true; }
        Check(rejected, "concurrent configuration writer is rejected before replacement");
    }
    foreach (var invalid in new[] { "{}", "{\"SchemaVersion\":1,\"MinimizeToTray\":true,\"PollIntervalSeconds\":3,\"Rpm\":5500}", "{\"SchemaVersion\":1,\"MinimizeToTray\":true,\"PollIntervalSeconds\":3,\"PollIntervalSeconds\":5}", new string('x', 16385) })
    {
        File.WriteAllText(Path.Combine(configDir, "bad.json"), invalid);
        rejected = false;
        try
        {
            PreferenceStore.Load(Path.Combine(configDir, "bad.json"));
        }
        catch (Exception) { rejected = true; }
        Check(rejected, "incomplete hardware duplicate or oversized settings rejected");
    }
}
finally { foreach (var f in Directory.GetFiles(configDir)) File.Delete(f); Directory.Delete(configDir); }
var validRequest = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "read-fans", device);
Check(BrokerProtocol.Valid(validRequest), "broker accepts read-only versioned request");
Check(!BrokerProtocol.Valid(validRequest with { Operation = "fan-manual" }), "broker rejects untyped write operation");
Check(!BrokerProtocol.Valid(validRequest with { Version = 99 }) && !BrokerProtocol.Valid(validRequest with { RequestId = "bad" }), "broker rejects version and request identifier mismatch");
foreach (var invalidIdentity in new[] { device with { Model = null! }, device with { Bios = "" }, device with { Product = new string('x', 161) }, device with { Manufacturer = "LENOVO\n" } })
    Check(!BrokerProtocol.Valid(validRequest with
    {
        Device = invalidIdentity
    }), "broker rejects incomplete or malformed identity before elevation");
var validWire = JsonSerializer.Serialize(validRequest, BrokerProtocol.Json);
foreach (var invalidWire in new[] { validWire.Replace("\"Version\":2", "\"Version\":1,\"Version\":2"), validWire.Replace("\"Operation\":\"read-fans\",", ""), validWire.Replace("\"Manufacturer\":\"LENOVO\"", "\"Manufacturer\":null") })
{
    using var malformed = new MemoryStream();
    var payload = System.Text.Encoding.UTF8.GetBytes(invalidWire);
    var header = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
    malformed.Write(header);
    malformed.Write(payload);
    malformed.Position = 0;
    bool rejected = false;
    try
    {
        await BrokerProtocol.ReceiveAsync<BrokerRequest>(malformed, default);
    }
    catch (JsonException) { rejected = true; }
    Check(rejected, "broker rejects duplicate missing or null required fields");
}
using (var frame = new MemoryStream())
{
    await BrokerProtocol.SendAsync(frame, validRequest, default);
    frame.Position = 0;
    Check(await BrokerProtocol.ReceiveAsync<BrokerRequest>(frame, default) == validRequest, "framed protocol round trip");
}
foreach (var length in new[] { -1, 0, 32769 })
{
    var bytes = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
    bool rejected = false;
    try
    {
        await BrokerProtocol.ReceiveAsync<BrokerRequest>(new MemoryStream(bytes), default);
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "invalid frame length rejected before allocation");
}
using (var frame = new MemoryStream())
{
    var bytes = System.Text.Encoding.UTF8.GetBytes("""{"Version":1,"RequestId":"id","Operation":"read-fans","Device":null,"Script":"bad"}""");
    var header = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
    frame.Write(header);
    frame.Write(bytes);
    frame.Position = 0;
    bool rejected = false;
    try
    {
        await BrokerProtocol.ReceiveAsync<BrokerRequest>(frame, default);
    }
    catch (JsonException) { rejected = true; }
    Check(rejected, "arbitrary extra request fields rejected");
}
using (var frame = new MemoryStream(new byte[] { 12, 0, 0, 0, 1 }))
{
    bool rejected = false;
    try
    {
        await BrokerProtocol.ReceiveAsync<BrokerRequest>(frame, default);
    }
    catch (EndOfStreamException) { rejected = true; }
    Check(rejected, "truncated frame fails closed");
}
if (OperatingSystem.IsWindows())
{
    var name = "pc-control-" + Guid.NewGuid().ToString("N");
    using var server = LocalPipe.Create(name);
    using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await Task.WhenAll(server.WaitForConnectionAsync(deadline.Token), client.ConnectAsync(deadline.Token));
    Check(LocalPipe.ClientIs(server, Environment.ProcessId) && LocalPipe.ServerIs(client, Environment.ProcessId), "OS peer process identifiers verified");
    Check(!LocalPipe.ClientIs(server, Environment.ProcessId + 1) && !LocalPipe.ServerIs(client, Environment.ProcessId + 1), "wrong peer process identifiers rejected");
    await BrokerProtocol.SendAsync(server, validRequest, deadline.Token);
    Check(await BrokerProtocol.ReceiveAsync<BrokerRequest>(client, deadline.Token) == validRequest, "real local pipe request round trip");
    using var cts = new CancellationTokenSource(20);
    bool cancelled = false;
    try
    {
        await BrokerProtocol.ReceiveAsync<BrokerResponse>(client, cts.Token);
    }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "stalled pipe read respects cancellation");
}
foreach (var trial in new[] { new FanTrial("manual", 3500, 4500, 12), new FanTrial("full", 0, 0, 5), new FanTrial("auto", 0, 0, 0) })
{
    Check(trial.IsValid && BrokerProtocol.Valid(validRequest with
    {
        Operation = "fan-trial",
        Trial = trial
    }), "typed bounded fan request accepted");
    using var trialFrame = new MemoryStream();
    var q = validRequest with
    {
        Operation = "fan-trial",
        Trial = trial
    };
    await BrokerProtocol.SendAsync(trialFrame, q, default);
    trialFrame.Position = 0;
    Check(await BrokerProtocol.ReceiveAsync<BrokerRequest>(trialFrame, default) == q, "fan trial frame round trip");
}
foreach (var trial in new[] { new FanTrial("manual", 1499, 4500, 12), new FanTrial("manual", 3500, 5501, 12), new FanTrial("manual", 3500, 4500, 31), new FanTrial("manual", 3500, 4500, 0), new FanTrial("full", 100, 0, 5), new FanTrial("auto", 0, 0, 10), new FanTrial("raw", 0, 0, 5) })
{
    Check(!trial.IsValid && !BrokerProtocol.Valid(validRequest with
    {
        Operation = "fan-trial",
        Trial = trial
    }), "unsafe or unbounded fan request rejected");
}
Check(!BrokerProtocol.Valid(validRequest with { Operation = "fan-trial" }), "fan request requires typed settings");
Check(!BrokerProtocol.Valid(validRequest with { Trial = new("auto", 0, 0, 0) }), "read request cannot smuggle fan settings");
Check((await FanSessionRunner.RunAsync(new("raw", 0, 0, 0), default)).Code == "INVALID_REQUEST", "invalid worker request never starts a process");
foreach (var m in new[] { 0, 1, 3 })
{
    var modeReq = new ModeRequest(m);
    Check(modeReq.IsValid && BrokerProtocol.Valid(validRequest with
    {
        Operation = "set-mode",
        Mode = modeReq
    }), "typed valid performance mode request accepted");
    using var modeFrame = new MemoryStream();
    var q = validRequest with
    {
        Operation = "set-mode",
        Mode = modeReq
    };
    await BrokerProtocol.SendAsync(modeFrame, q, default);
    modeFrame.Position = 0;
    Check(await BrokerProtocol.ReceiveAsync<BrokerRequest>(modeFrame, default) == q, "mode request frame round trip");
}
foreach (var m in new[] { -1, 2, 4, 99 })
{
    var badReq = new ModeRequest(m);
    Check(!badReq.IsValid && !BrokerProtocol.Valid(validRequest with
    {
        Operation = "set-mode",
        Mode = badReq
    }), "invalid performance mode request rejected");
}
Check(!BrokerProtocol.Valid(validRequest with { Operation = "set-mode" }), "mode request requires typed settings");
Check(!BrokerProtocol.Valid(validRequest with { Mode = new(0) }), "read request cannot smuggle mode settings");
Check(!BrokerProtocol.Valid(validRequest with { Operation = "set-mode", Mode = new(0), Trial = new("auto", 0, 0, 0) }), "cannot smuggle fan settings in mode request");
Check((await ModeSessionRunner.RunAsync(new(99), default)).Code == "INVALID_REQUEST", "invalid mode worker request never starts a process");
foreach (var (k, v) in new[] { ("charge", 1), ("key", 2), ("night", 1) })
{
    var energyReq = new EnergyRequest(k, v);
    Check(energyReq.IsValid && BrokerProtocol.Valid(validRequest with
    {
        Operation = "set-energy",
        Energy = energyReq
    }), "typed valid energy request accepted");
    using var energyFrame = new MemoryStream();
    var q = validRequest with
    {
        Operation = "set-energy",
        Energy = energyReq
    };
    await BrokerProtocol.SendAsync(energyFrame, q, default);
    energyFrame.Position = 0;
    Check(await BrokerProtocol.ReceiveAsync<BrokerRequest>(energyFrame, default) == q, "energy request frame round trip");
}
foreach (var (k, v) in new[] { ("charge", 3), ("key", 4), ("night", 2), ("invalid", 0) })
{
    var badReq = new EnergyRequest(k, v);
    Check(!badReq.IsValid && !BrokerProtocol.Valid(validRequest with
    {
        Operation = "set-energy",
        Energy = badReq
    }), "invalid energy request rejected");
}
Check(!BrokerProtocol.Valid(validRequest with { Operation = "set-energy" }), "energy request requires typed settings");
Check(!BrokerProtocol.Valid(validRequest with { Energy = new("charge", 1) }), "read request cannot smuggle energy settings");
Check(!BrokerProtocol.Valid(validRequest with { Operation = "set-energy", Energy = new("charge", 1), Mode = new(1) }), "cannot smuggle mode settings in energy request");
Check((await EnergySessionRunner.RunAsync(new("invalid", 0), default)).Code == "INVALID_REQUEST", "invalid energy worker request never starts a process");
Check(PublicText.Clean("\nhello\r\t") == "hello" && PublicText.Clean(new string('x', 300)).Length == 160, "public device text strips controls and bounds size");
using (var report = JsonDocument.Parse(Diagnostics.ToJson(snap with
{
    System = new("10.0", "26100", "CPU", "Maker", "Board", [new("GPU", "32.0.1")])
})))
{
    Check(report.RootElement.GetProperty("schemaVersion").GetInt32() == 2 && report.RootElement.GetProperty("system").GetProperty("Displays")[0].GetProperty("DriverVersion").GetString() == "32.0.1", "diagnostic schema includes selected platform and driver fields");
}
var reportPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
void WriteReport(string content, string name = "diagnostics.json")
{
    if (File.Exists(reportPath))
        File.Delete(reportPath);
    using var archive = ZipFile.Open(reportPath, ZipArchiveMode.Create);
    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
    writer.Write(content);
}
try
{
    WriteReport(Diagnostics.ToJson(snap));
    var summary = Feedback.Inspect(reportPath);
    Check(summary.ReportedDevice == device && summary.Trust == "UserReportedUnverified", "feedback stays untrusted even for known model");
    WriteReport(Diagnostics.ToJson(snap with
    {
        System = new("10.0", "26100", "CPU", "Maker", "Board", [new("GPU", "32.0.1", "InstalledDriverRegistry")])
    }));
    Check(Feedback.Inspect(reportPath).ReportedSystem?.Displays[0] is { DriverVersion: "32.0.1", Source: "InstalledDriverRegistry" }, "feedback retains bounded driver details and provenance");
    WriteReport(Diagnostics.ToJson(snap).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1"));
    Check(Feedback.Inspect(reportPath) is { SchemaVersion: 1, ReportedSystem: null }, "legacy feedback remains readable without invented system details");
    var firstKey = summary.CompatibilityKey;
    WriteReport(Diagnostics.ToJson(snap with
    {
        Device = device with
        {
            Bios = "Other"
        }
    }));
    Check(Feedback.Inspect(reportPath).CompatibilityKey != firstKey, "firmware changes produce separate compatibility group");
    foreach (var invalid in new[] { Diagnostics.ToJson(snap).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99"), "{\"schemaVersion\":2,\"schemaVersion\":1}", new string('x', 262145) })
    {
        WriteReport(invalid);
        bool rejected = false;
        try
        {
            Feedback.Inspect(reportPath);
        }
        catch (Exception) { rejected = true; }
        Check(rejected, "unsupported duplicate or oversized feedback rejected");
    }
    WriteReport(Diagnostics.ToJson(snap), "../../unexpected.json");
    bool badEntry = false;
    try
    {
        Feedback.Inspect(reportPath);
    }
    catch (InvalidDataException) { badEntry = true; }
    Check(badEntry, "path traversal archive entry rejected without extraction");
    WriteReport(Diagnostics.ToJson(snap));
    using (var archive = ZipFile.Open(reportPath, ZipArchiveMode.Update))
    {
        archive.CreateEntry("tool.exe");
    }
    badEntry = false;
    try
    {
        Feedback.Inspect(reportPath);
    }
    catch (InvalidDataException) { badEntry = true; }
    Check(badEntry, "extra executable entry rejected without execution");
}
finally { if (File.Exists(reportPath)) File.Delete(reportPath); }
await FanWorkerTests.RunAllAsync(Check);
await ModeWorkerTests.RunAllAsync(Check);
await EnergyWorkerTests.RunAllAsync(Check);
await DesktopViewModelTests.RunAllAsync(Check);
DesktopFeedbackTests.Run(Check, snap);
await BrightnessScriptTests.RunAllAsync(Check);
FanStatusMapperTests.RunAll(Check);
await RecoveryLifecycleTests.RunAllAsync(Check);
foreach (var badInterval in new[] { 0, 31 })
{
    bool rejected = false;
    try
    {
        _ = new MonitorSession(realProvider, device, TimeSpan.FromSeconds(badInterval));
    }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "monitor rejects invalid polling intervals");
}
var monitorProvider = new FakeMonitorProvider(device);
var monitor = new MonitorSession(monitorProvider, device, TimeSpan.FromSeconds(1));
using (var cancel = new CancellationTokenSource())
{
    await using var stream = monitor.WatchAsync(cancel.Token).GetAsyncEnumerator();
    Check(await stream.MoveNextAsync() && stream.Current.Device == device, "monitor yields first read without an initial delay");
    await Task.Delay(30);
    Check(monitorProvider.Reads == 1, "slow consumer does not accumulate background reads");
    await using (var duplicate = monitor.WatchAsync().GetAsyncEnumerator())
    {
        bool rejected = false;
        try
        {
            await duplicate.MoveNextAsync();
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "monitor rejects a second simultaneous consumer");
    }
    cancel.CancelAfter(20);
    bool cancelled = false;
    try
    {
        await stream.MoveNextAsync();
    }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && monitorProvider.Reads == 1, "monitor cancellation interrupts polling delay without another read");
}
await using (var restarted = monitor.WatchAsync().GetAsyncEnumerator())
{
    Check(await restarted.MoveNextAsync() && monitorProvider.Reads == 2, "disposed monitor can be restarted");
}
monitorProvider.ChangeIdentity = true;
await using (var mismatched = monitor.WatchAsync().GetAsyncEnumerator())
{
    bool rejected = false;
    try
    {
        await mismatched.MoveNextAsync();
    }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "monitor rejects changed identity before emitting sample");
}
await SliderQueueTests.RunAsync(device, Check);
await GpuTests.RunAsync(device, Check);
Console.WriteLine($"{passed} tests passed");

sealed class FakeProbe : IReadOnlyProbe
{
    public bool FailFans, BadFans, Denied, BadGeneric;
    public Task<JsonElement> QueryAsync(ProbeKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (kind == ProbeKind.ThinkBookMode)
            return Task.FromResult(JsonDocument.Parse("""{"mode":0,"serviceVersion":8193}""").RootElement.Clone());
        if (kind == ProbeKind.ThinkBookFans && Denied)
            throw new ProbeException("ACCESS_DENIED");
        if (kind == ProbeKind.Generic && BadGeneric)
            return Task.FromResult(JsonDocument.Parse("""{"memory":101,"battery":-1,"brightness":null}""").RootElement.Clone());
        if (kind == ProbeKind.ThinkBookFans && FailFans)
            throw new IOException("secret path");
        using var d = JsonDocument.Parse(kind == ProbeKind.ThinkBookFans ? (BadFans ? "{\"fan1\":-1,\"fan2\":4500}" : "{\"fan1\":3500,\"fan2\":4500}") : "{\"memory\":40,\"battery\":90,\"brightness\":75}");
        return Task.FromResult(d.RootElement.Clone());
    }
}
sealed class FakeWriter(DeviceIdentity identity) : IHardwareProvider
{
    public string Id => "test"; public bool Matches(DeviceIdentity d) => true;
    public double Max = 5500; public int Writes, MaxConcurrent; private int concurrent;
    public bool ChangeDevice, CancelDuringWrite; public SupportLevel Level = SupportLevel.Verified;
    public Task<Snapshot> ReadAsync(DeviceIdentity d, CancellationToken ct) => Task.FromResult(new Snapshot(Id, ChangeDevice ? identity with { Bios = "new" } : identity, [new(Feature.FanControl, Level, true, "RPM", 1500, Max)], [], []));
    public async Task<ControlResult> ApplyAsync(DeviceIdentity d, ControlRequest r, CancellationToken ct)
    {
        Writes++;
        var n = Interlocked.Increment(ref concurrent);
        MaxConcurrent = Math.Max(MaxConcurrent, n);
        try
        {
            await Task.Delay(10, ct);
            if (CancelDuringWrite)
                throw new OperationCanceledException();
            return new(ResultCode.Success, "read back confirmed");
        }
        finally { Interlocked.Decrement(ref concurrent); }
    }
}

sealed class FakeMonitorProvider(DeviceIdentity identity) : IHardwareProvider
{
    public int Reads; public bool ChangeIdentity;
    public string Id => "monitor-test";
    public bool Matches(DeviceIdentity device) => true;
    public Task<Snapshot> ReadAsync(DeviceIdentity device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Reads++;
        return Task.FromResult(new Snapshot(Id, ChangeIdentity ? identity with
        {
            Bios = "changed"
        } : identity, [], [], []));
    }
    public Task<ControlResult> ApplyAsync(DeviceIdentity device, ControlRequest request, CancellationToken ct) => throw new Exception("Monitor must never write");
}
