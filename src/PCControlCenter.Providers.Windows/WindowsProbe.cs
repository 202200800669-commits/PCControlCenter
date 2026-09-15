using System.Text.Json.Nodes;
using System.Runtime.Versioning;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PCControlCenter.Core;

namespace PCControlCenter.Providers.Windows;

public sealed class ProbeException(string code) : IOException(code)
{
    public string Code { get; } = code;
}
public enum ProbeKind
{
    Identity, Generic, ThinkBookFans, ThinkBookMode
}
public interface IReadOnlyProbe
{
    Task<JsonElement> QueryAsync(ProbeKind kind, CancellationToken ct);
}

public sealed class WindowsProbe : IReadOnlyProbe
{
    // These scripts are compiled into the application. There is no arbitrary script API.
    private const string Prelude = "[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false);$ErrorActionPreference='Stop';";
    private const string IdentityScript = """
 $c=Get-CimInstance Win32_ComputerSystem -Property Manufacturer -OperationTimeoutSec 4
 $p=Get-CimInstance Win32_ComputerSystemProduct -Property Name,Version -OperationTimeoutSec 4
 $b=Get-CimInstance Win32_BIOS -Property SMBIOSBIOSVersion -OperationTimeoutSec 4
 @{manufacturer=[string]$c.Manufacturer;product=[string]$p.Name;model=[string]$p.Version;bios=[string]$b.SMBIOSBIOSVersion;platform='Windows'}|ConvertTo-Json -Compress
 """;
    private const string GenericScript = """
 $r=@{memory=$null;battery=$null;brightness=$null;osVersion="";osBuild="";cpuName="";boardMaker="";boardProduct="";displays=@()}
 try{$o=Get-CimInstance Win32_OperatingSystem -Property FreePhysicalMemory,TotalVisibleMemorySize,Version,BuildNumber -OperationTimeoutSec 4;$r.memory=[math]::Round((1-$o.FreePhysicalMemory/$o.TotalVisibleMemorySize)*100,2);$r.osVersion=[string]$o.Version;$r.osBuild=[string]$o.BuildNumber}catch{}
 try{$a=@(Get-CimInstance Win32_Battery -Property EstimatedChargeRemaining -OperationTimeoutSec 4);if($a.Count -eq 1){$r.battery=[int]$a[0].EstimatedChargeRemaining}}catch{}
 try{$a=@(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -OperationTimeoutSec 4|Where-Object Active);if($a.Count -eq 1){$r.brightness=[int]$a[0].CurrentBrightness}}catch{}
 try{$cpu=Get-CimInstance Win32_Processor -Property Name -OperationTimeoutSec 3|Select-Object -First 1;$r.cpuName=[string]$cpu.Name}catch{}
 try{$board=Get-CimInstance Win32_BaseBoard -Property Manufacturer,Product -OperationTimeoutSec 3|Select-Object -First 1;$r.boardMaker=[string]$board.Manufacturer;$r.boardProduct=[string]$board.Product}catch{}
 $r|ConvertTo-Json -Depth 4 -Compress
 """;
    private const string ModeScript = """
 $c=Get-CimInstance Win32_ComputerSystem -Property Manufacturer -OperationTimeoutSec 3
 $p=Get-CimInstance Win32_ComputerSystemProduct -Property Name,Version -OperationTimeoutSec 3
 $b=Get-CimInstance Win32_BIOS -Property SMBIOSBIOSVersion -OperationTimeoutSec 3
 if($c.Manufacturer -ne 'LENOVO' -or $p.Name -ne '21R0' -or $p.Version -ne 'ThinkBook 16p G6 IAX' -or $b.SMBIOSBIOSVersion -ne 'R2CN57WW'){throw 'IDENTITY_MISMATCH'}
 $k=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider'
 if($k.Version -lt 8192 -or (Get-Service LenovoProcessManagement).Status -ne 'Running'){throw 'SERVICE_UNAVAILABLE'}
 $mode=$k.ITS_CurrentSetting
 if(($k.ITS_FN_Capability -band 16) -ne 0){$mode=$k.ITS_CurrentSettingV}
 if($null -eq $mode -or $mode -notin @(0,1,3,4)){throw 'UNKNOWN_MODE'}
 @{mode=[int]$mode;serviceVersion=[int]$k.Version}|ConvertTo-Json -Compress
 """;
    private const string FanScript = """
 $c=Get-CimInstance Win32_ComputerSystem -Property Manufacturer -OperationTimeoutSec 4
 $p=Get-CimInstance Win32_ComputerSystemProduct -Property Name,Version -OperationTimeoutSec 4
 $b=Get-CimInstance Win32_BIOS -Property SMBIOSBIOSVersion -OperationTimeoutSec 4
 if($c.Manufacturer -ne 'LENOVO' -or $p.Name -ne '21R0' -or $p.Version -ne 'ThinkBook 16p G6 IAX' -or $b.SMBIOSBIOSVersion -ne 'R2CN57WW'){throw 'IDENTITY_MISMATCH'}
 $a=@(Get-CimInstance -Namespace root/wmi -ClassName LENOVO_OTHER_METHOD -OperationTimeoutSec 4|Where-Object Active)
 $l=@(Get-CimInstance -Namespace root/wmi -ClassName LENOVO_FAN_TEST_DATA -OperationTimeoutSec 4|Where-Object Active)
 if($a.Count -ne 1 -or $l.Count -ne 1){throw 'INTERFACE_UNAVAILABLE'}
 if($l[0].FanId.Count -ne 2 -or $l[0].FanId[0] -ne 1 -or $l[0].FanId[1] -ne 2 -or $l[0].FanMinSpeed.Count -ne 2 -or $l[0].FanMaxSpeed.Count -ne 2){throw 'LIMITS_CHANGED'}
 for($i=0;$i -lt 2;$i++){if($l[0].FanMinSpeed[$i] -ne 1500 -or $l[0].FanMaxSpeed[$i] -ne 5500){throw 'LIMITS_CHANGED'}}
 $r=@{}
 foreach($entry in @(@('fan1',0x04030001),@('fan2',0x04030002))){
  $v=Invoke-CimMethod -InputObject $a[0] -MethodName GetFeatureValue -Arguments @{IDs=[uint32]$entry[1]} -OperationTimeoutSec 4
  if($v.ReturnValue -ne $true -or $null -eq $v.value -or $v.value -gt 10000){throw 'INVALID_READING'}
  $r[$entry[0]]=[int]$v.value
 }
 $r|ConvertTo-Json -Compress
 """;
    public async Task<JsonElement> QueryAsync(ProbeKind kind, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var script = kind switch
        {
            ProbeKind.Identity => IdentityScript,
            ProbeKind.Generic => GenericScript,
            ProbeKind.ThinkBookFans => FanScript,
            ProbeKind.ThinkBookMode => ModeScript,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Prelude + "try {" + script + "} catch { $code='PROBE_FAILED'; if($_.Exception.NativeErrorCode -eq 'AccessDenied' -or $_.CategoryInfo.Category -eq 'PermissionDenied' -or $_.Exception.HResult -in @(-2147024891,-2147217405)){$code='ACCESS_DENIED'}; [Console]::Error.WriteLine($code); exit 1 }")) })
            start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new IOException("PROBE_START_FAILED");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            var error = await errors;
            if (process.ExitCode != 0)
                throw new ProbeException(error.Contains("ACCESS_DENIED", StringComparison.Ordinal) ? "ACCESS_DENIED" : "PROBE_FAILED");
            if (text.Length > 65536)
                throw new IOException("PROBE_OUTPUT_TOO_LARGE");
            if (kind == ProbeKind.Generic)
            {
                var node = JsonNode.Parse(text) ?? throw new IOException("INVALID_PROBE_JSON");
                node["displays"] = InstalledDisplayDrivers();
                node["gpuTelemetry"] = JsonSerializer.SerializeToNode(await NvidiaTelemetry.ReadAsync(ct));
                text = node.ToJsonString();
            }
            using var json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException) { }
            try
            {
                await Task.WhenAll(output, errors);
            }
            catch (Exception) { }
            throw;
        }
    }
    [SupportedOSPlatform("windows")]
    private static JsonArray InstalledDisplayDrivers()
    {
        var result = new JsonArray();
        // Registry inventory is explicitly labelled installed, not currently active.
        try
        {
            using var drivers = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (drivers is null)
                return result;
            foreach (var name in drivers.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)).Order())
            {
                using var key = drivers.OpenSubKey(name);
                if (key is null)
                    continue;
                var description = PublicText.Clean(key.GetValue("DriverDesc", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                var version = PublicText.Clean(key.GetValue("DriverVersion", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                if (description.Length == 0 || version.Length == 0)
                    continue;
                result.Add(new JsonObject { ["name"] = description, ["driverVersion"] = version, ["source"] = "InstalledDriverRegistry" });
                if (result.Count == 8)
                    break;
            }
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return result;
    }
    public static async Task<DeviceIdentity> IdentifyAsync(IReadOnlyProbe probe, CancellationToken ct)
    {
        var json = await probe.QueryAsync(ProbeKind.Identity, ct);
        string Field(string key)
        {
            var text = json.GetProperty(key).GetString() ?? "";
            return new string(text.Where(c => !char.IsControl(c)).Take(120).ToArray());
        }
        return new(Field("manufacturer"), Field("product"), Field("model"), Field("bios"), Field("platform"));
    }
}
