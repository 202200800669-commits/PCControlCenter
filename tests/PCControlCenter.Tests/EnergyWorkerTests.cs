using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PCControlCenter.Providers.Windows;

static class EnergyWorkerTests
{
    private const string MockEnvironment = @"
 $script:energyWrites=New-Object Collections.Generic.List[object]
 $script:simCharge=0
 $script:simKey=0
 $script:simNight=0

 function Get-CimInstance {
  param([string]$ClassName,[string]$Namespace,[int]$OperationTimeoutSec,[string[]]$Property)
  switch($ClassName) {
   'Win32_ComputerSystem' {if($wrongModel){return [pscustomobject]@{Manufacturer='OTHER'}};return [pscustomobject]@{Manufacturer='LENOVO'}}
   'Win32_ComputerSystemProduct' {return [pscustomobject]@{Name='21R0';Version='ThinkBook 16p G6 IAX'}}
   'Win32_BIOS' {return [pscustomobject]@{SMBIOSBIOSVersion='R2CN57WW'}}
   default {throw 'UNMOCKED_CLASS'}
  }
 }

 function Invoke-EnergyRead([string]$k) {
  switch($k) {
   'charge' {return $script:simCharge}
   'night'  {return $script:simNight}
   'key'    {return $script:simKey}
   default  {throw 'INVALID_KIND'}
  }
 }

 function Invoke-EnergySet([string]$k, [int]$v) {
  $script:energyWrites.Add([pscustomobject]@{kind=$k;value=$v})
  if($failCommand){throw 'SET_FAILED'}
  if(!$failConfirm){
   switch($k) {
    'charge' {$script:simCharge=$v}
    'night'  {$script:simNight=$v}
    'key'    {$script:simKey=$v}
   }
  }
 }

 function Start-Sleep {param([int]$Milliseconds) [Threading.Thread]::Sleep(2)}
";

    private sealed record Scenario(string Kind, int TargetValue, int InitialValue = 0, bool WrongModel = false, bool FailCommand = false, bool FailConfirm = false);

    private static async Task<(JsonElement Receipt, JsonElement Writes)> RunAsync(Scenario s, string? fanMutex = null, string? energyMutex = null, bool closeStdinBeforeWrite = false)
    {
        using var source = typeof(EnergySessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.EnergySession.ps1")!;
        using var reader = new StreamReader(source);
        var script = await reader.ReadToEndAsync();
        const string prodFanMutex = @"Global\PCControlCenter.ThinkBookFanSession";
        const string prodEnergyMutex = @"Global\PCControlCenter.ThinkBookEnergySession";
        if (!script.Contains(prodFanMutex) || !script.Contains(prodEnergyMutex))
            throw new Exception("Energy session mutex changed; test isolation must be reviewed");

        script = script.Replace(prodFanMutex, fanMutex ?? "Local\\PCControlCenter.TestFan." + Guid.NewGuid().ToString("N"));
        script = script.Replace(prodEnergyMutex, energyMutex ?? "Local\\PCControlCenter.TestEnergy." + Guid.NewGuid().ToString("N"));

        var prefix = $"$kind='{s.Kind}';$value={s.TargetValue};$wrongModel=${s.WrongModel};$failCommand=${s.FailCommand};$failConfirm=${s.FailConfirm};";
        if (s.Kind == "charge")
            prefix += $"$script:simCharge={s.InitialValue};";
        else if (s.Kind == "key")
            prefix += $"$script:simKey={s.InitialValue};";
        else if (s.Kind == "night")
            prefix += $"$script:simNight={s.InitialValue};";

        var body = MockEnvironment + "\n" + prefix + "\n" + script + "\nConvertTo-Json -InputObject @($script:energyWrites.ToArray()) -Compress";

        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(body)) })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new Exception("Test process failed");
        if (closeStdinBeforeWrite)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch { }
        }
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (process.ExitCode != 0)
                throw new Exception("Mock worker failed: " + await errors);
            var lines = (await output).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 2)
                throw new Exception("Unexpected mock output: " + string.Join(" / ", lines));
            return (JsonDocument.Parse(lines[0]).RootElement.Clone(), JsonDocument.Parse(lines[1]).RootElement.Clone());
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(true);
        }
    }

    public static async Task RunAllAsync(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var cancelledBeforeWrite = await RunAsync(new("charge", 1, 0), closeStdinBeforeWrite: true);
        check(cancelledBeforeWrite.Receipt.GetProperty("Code").GetString() == "CANCELLED_BEFORE_WRITE" &&
              cancelledBeforeWrite.Receipt.GetProperty("Recovery").GetString() == "NOT_NEEDED" &&
              cancelledBeforeWrite.Writes.GetArrayLength() == 0, "pre-write cancellation in energy worker aborts with zero writes");

        var charge = await RunAsync(new("charge", 1, 0));
        check(charge.Receipt.GetProperty("Code").GetString() == "COMPLETED" &&
              charge.Receipt.GetProperty("FinalValue").GetInt32() == 1 &&
              charge.Writes.GetArrayLength() == 1 &&
              charge.Writes[0].GetProperty("value").GetInt32() == 1, "energy charge switch from 0 to 1 completes");

        var key = await RunAsync(new("key", 2, 0));
        check(key.Receipt.GetProperty("Code").GetString() == "COMPLETED" &&
              key.Receipt.GetProperty("FinalValue").GetInt32() == 2 &&
              key.Writes.GetArrayLength() == 1 &&
              key.Writes[0].GetProperty("value").GetInt32() == 2, "energy key backlight switch completes");

        var redundant = await RunAsync(new("charge", 1, 1));
        check(redundant.Receipt.GetProperty("Code").GetString() == "COMPLETED" &&
              redundant.Writes.GetArrayLength() == 0, "redundant energy switch sends zero writes");

        var wrong = await RunAsync(new("charge", 1, 0, WrongModel: true));
        check(wrong.Receipt.GetProperty("Code").GetString() == "IDENTITY_MISMATCH" &&
              wrong.Writes.GetArrayLength() == 0, "wrong identity in energy worker causes zero writes");

        var rollback = await RunAsync(new("charge", 1, 0, FailConfirm: true));
        check(rollback.Receipt.GetProperty("Code").GetString() == "TARGET_NOT_REACHED" &&
              rollback.Receipt.GetProperty("Recovery").GetString() == "RESTORED_PREVIOUS" &&
              rollback.Writes.GetArrayLength() >= 2, "energy confirmation timeout triggers rollback to previous value");

        var fanLockName = "Local\\PCControlCenter.TestFan." + Guid.NewGuid().ToString("N");
        using (var fanMutex = new Mutex(true, fanLockName))
        {
            var busyFan = await RunAsync(new("charge", 1, 0), fanMutex: fanLockName);
            check(busyFan.Receipt.GetProperty("Code").GetString() == "BUSY" &&
                  busyFan.Writes.GetArrayLength() == 0, "active fan session blocks energy writes");
        }

        var energyLockName = "Local\\PCControlCenter.TestEnergy." + Guid.NewGuid().ToString("N");
        using (var energyMutex = new Mutex(true, energyLockName))
        {
            var busyEnergy = await RunAsync(new("charge", 1, 0), energyMutex: energyLockName);
            check(busyEnergy.Receipt.GetProperty("Code").GetString() == "BUSY" &&
                  busyEnergy.Writes.GetArrayLength() == 0, "concurrent energy session mutex blocks second write");
        }
    }
}
