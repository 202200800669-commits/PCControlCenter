using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PCControlCenter.Providers.Windows;

static class FanWorkerTests
{
    private const string MockHardware = """
 $script:writes=New-Object Collections.Generic.List[object]
 $script:values=@{[uint32]0x04020000=0;[uint32]0x04030001=2500;[uint32]0x04030002=2300}
 function Get-CimInstance {
  param([string]$ClassName,[string]$Namespace,[int]$OperationTimeoutSec,[string[]]$Property)
  switch($ClassName) {
   'Win32_ComputerSystem' {if($wrongModel){return [pscustomobject]@{Manufacturer='OTHER'}};return [pscustomobject]@{Manufacturer='LENOVO'}}
   'Win32_ComputerSystemProduct' {return [pscustomobject]@{Name='21R0';Version='ThinkBook 16p G6 IAX'}}
   'Win32_BIOS' {return [pscustomobject]@{SMBIOSBIOSVersion='R2CN57WW'}}
   'LENOVO_OTHER_METHOD' {return [pscustomobject]@{Active=$true}}
   'LENOVO_FAN_TEST_DATA' {return [pscustomobject]@{Active=$true;FanId=@(1,2);FanMinSpeed=@(1500,1500);FanMaxSpeed=@(5500,5500)}}
   default {throw 'UNMOCKED_CLASS'}
  }
 }
 function Invoke-CimMethod {
  param($InputObject,[string]$MethodName,$Arguments,[int]$OperationTimeoutSec)
  $id=[uint32]$Arguments.IDs
  if($MethodName -eq 'GetFeatureValue') {return [pscustomobject]@{ReturnValue=$true;value=$script:values[$id]}}
  if($MethodName -ne 'SetFeatureValue'){throw 'UNMOCKED_METHOD'}
  $script:writes.Add([pscustomobject]@{id=$id;value=[int]$Arguments.value})
  if($signal -and $script:writes.Count -eq 1){[Console]::WriteLine('READY')}
  if($script:writes.Count -eq $failAt){throw 'INJECTED_FAILURE'}
  $script:values[$id]=[int]$Arguments.value
 }
 function Start-Sleep {param([int]$Milliseconds) [Threading.Thread]::Sleep(5)}
 """;
    private sealed record Scenario(string Mode, int FailAt, bool WrongModel = false, bool Disconnect = false);
    private static async Task<(JsonElement Receipt, JsonElement Writes)> RunAsync(Scenario s, string? mutexName = null)
    {
        using var source = typeof(FanSessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.FanSession.ps1")!;
        using var reader = new StreamReader(source);
        var script = await reader.ReadToEndAsync();
        const string productionMutex = @"Global\PCControlCenter.ThinkBookFanSession";
        if (!script.Contains(productionMutex))
            throw new Exception("Worker mutex changed; test isolation must be reviewed");
        script = script.Replace(productionMutex, mutexName ?? "Local\\PCControlCenter.Test." + Guid.NewGuid().ToString("N"));
        var prefix = $"$mode='{s.Mode}';$rpm1={(s.Mode == "manual" ? 3500 : 0)};$rpm2={(s.Mode == "manual" ? 4500 : 0)};$seconds={(s.Mode == "manual" ? 5 : 0)};$failAt={s.FailAt};$wrongModel=${s.WrongModel};$signal=${s.Disconnect};";
        var body = prefix + MockHardware + "\n" + script + "\nConvertTo-Json -InputObject @($script:writes.ToArray()) -Compress";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(body)) })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new Exception("Test process failed");
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            if (s.Disconnect)
            {
                var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
                if (ready != "READY")
                    throw new Exception("Worker did not enter simulated write");
                process.StandardInput.Close();
            }
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (process.ExitCode != 0)
                throw new Exception("Mock worker failed: " + await errors);
            var lines = (await output).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 2)
                throw new Exception("Unexpected mock output: " + string.Join(" / ", lines));
            return (JsonDocument.Parse(lines[0]).RootElement.Clone(), JsonDocument.Parse(lines[1]).RootElement.Clone());
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public static async Task RunAllAsync(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var normal = await RunAsync(new("auto", 0));
        check(normal.Receipt.GetProperty("Code").GetString() == "COMPLETED" && normal.Writes.GetArrayLength() == 6, "mock worker restores all three auto channels");
        var wrong = await RunAsync(new("auto", 0, WrongModel: true));
        check(wrong.Writes.GetArrayLength() == 0 && wrong.Receipt.GetProperty("Recovery").GetString() == "NOT_NEEDED", "wrong real-worker identity causes zero writes");
        foreach (var failAt in new[] { 1, 2, 3 })
        {
            var failed = await RunAsync(new("manual", failAt));
            var writes = failed.Writes.EnumerateArray().ToArray();
            check(failed.Receipt.GetProperty("Code").GetString() == "CONTROL_FAILED" && writes.Length == failAt + 3 && writes.TakeLast(3).All(w => w.GetProperty("value").GetInt32() == 0), "partial write failure attempts all recovery channels");
        }
        foreach (var failAt in new[] { 4, 5, 6 })
        {
            var failed = await RunAsync(new("auto", failAt));
            check(failed.Writes.GetArrayLength() == 6 && failed.Receipt.GetProperty("Recovery").GetString() == "UNCONFIRMED", "recovery failure preserves remaining attempts and reports unconfirmed");
        }
        var disconnected = await RunAsync(new("manual", 0, Disconnect: true));
        check(disconnected.Receipt.GetProperty("Code").GetString() == "INTERRUPTED" && disconnected.Receipt.GetProperty("Recovery").GetString() == "AUTO_COMMANDS_SENT_OVERRIDE_OFF", "stdin disconnect runs real worker recovery finally");
        var name = "Local\\PCControlCenter.Test." + Guid.NewGuid().ToString("N");
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? holderError = null;
        var holder = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(false, name);
                mutex.WaitOne();
                try
                {
                    acquired.Set();
                    release.Wait();
                }
                finally { mutex.ReleaseMutex(); }
            }
            catch (Exception e) { holderError = e; acquired.Set(); }
        });
        holder.Start();
        try
        {
            if (!acquired.Wait(TimeSpan.FromSeconds(5)) || holderError is not null)
                throw new Exception("Test mutex acquisition failed", holderError);
            var busy = await RunAsync(new("auto", 0), name);
            check(busy.Receipt.GetProperty("Code").GetString() == "BUSY" && busy.Writes.GetArrayLength() == 0, "occupied cross-process mutex causes zero hardware writes");
        }
        finally { release.Set(); holder.Join(); }
    }
}
