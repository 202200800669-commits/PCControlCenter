using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PCControlCenter.Providers.Windows;

static class ModeWorkerTests
{
    private const string MockEnvironment = @"
 $script:commands=New-Object Collections.Generic.List[int]
 $script:simulatedMode=0
 function Get-CimInstance {
  param([string]$ClassName,[string]$Namespace,[int]$OperationTimeoutSec,[string[]]$Property)
  switch($ClassName) {
   'Win32_ComputerSystem' {if($wrongModel){return [pscustomobject]@{Manufacturer='OTHER'}};return [pscustomobject]@{Manufacturer='LENOVO'}}
   'Win32_ComputerSystemProduct' {return [pscustomobject]@{Name='21R0';Version='ThinkBook 16p G6 IAX'}}
   'Win32_BIOS' {return [pscustomobject]@{SMBIOSBIOSVersion='R2CN57WW'}}
   default {throw 'UNMOCKED_CLASS'}
  }
 }
 function Get-ItemProperty {
  param([string]$LiteralPath)
  if($LiteralPath -like '*PowerSlider*'){
   if($serviceDown){return [pscustomobject]@{Version=1000}}
   return [pscustomobject]@{Version=8193;ITS_FN_Capability=26;ITS_CurrentSettingV=$script:simulatedMode;ITS_CurrentSetting=$script:simulatedMode}
  }
  throw 'UNMOCKED_PATH'
 }
 function Get-Service {
  param([string]$Name)
  if($serviceDown){return [pscustomobject]@{Status='Stopped'}}
  return [pscustomobject]@{Status='Running'}
 }
 function Send-ItsCommand([int]$m) {
  $cmd=@{0=163;1=164;3=165}[$m]
  $script:commands.Add($cmd)
  if($failCommand){throw 'COMMAND_FAILED'}
  if(!$failConfirm){
   $script:simulatedMode=$m
  }
 }
 function Start-Sleep {param([int]$Milliseconds) [Threading.Thread]::Sleep(2)}
";

    private sealed record Scenario(int TargetMode, int InitialMode = 0, bool WrongModel = false, bool ServiceDown = false, bool FailCommand = false, bool FailConfirm = false);

    private static async Task<(JsonElement Receipt, JsonElement Commands)> RunAsync(Scenario s, string? fanMutex = null, string? modeMutex = null)
    {
        using var source = typeof(ModeSessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.ModeSession.ps1")!;
        using var reader = new StreamReader(source);
        var script = await reader.ReadToEndAsync();
        const string prodFanMutex = @"Global\PCControlCenter.ThinkBookFanSession";
        const string prodModeMutex = @"Global\PCControlCenter.ThinkBookModeSession";
        if (!script.Contains(prodFanMutex) || !script.Contains(prodModeMutex))
            throw new Exception("Mode session mutex changed; test isolation must be reviewed");

        script = script.Replace(prodFanMutex, fanMutex ?? "Local\\PCControlCenter.TestFan." + Guid.NewGuid().ToString("N"));
        script = script.Replace(prodModeMutex, modeMutex ?? "Local\\PCControlCenter.TestMode." + Guid.NewGuid().ToString("N"));

        var prefix = $"$targetMode={s.TargetMode};$script:simulatedMode={s.InitialMode};$wrongModel=${s.WrongModel};$serviceDown=${s.ServiceDown};$failCommand=${s.FailCommand};$failConfirm=${s.FailConfirm};";
        var body = prefix + MockEnvironment + "\n" + script + "\nConvertTo-Json -InputObject @($script:commands.ToArray()) -Compress";

        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(body)) })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new Exception("Test process failed");
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
            if (!process.HasExited) process.Kill(true);
        }
    }

    public static async Task RunAllAsync(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var normal = await RunAsync(new(TargetMode: 1, InitialMode: 0));
        check(normal.Receipt.GetProperty("Code").GetString() == "COMPLETED" &&
              normal.Receipt.GetProperty("FinalMode").GetInt32() == 1 &&
              normal.Commands.GetArrayLength() == 1 &&
              normal.Commands[0].GetInt32() == 164, "mode switch 0 to 1 sends command 164 and completes");

        var redundant = await RunAsync(new(TargetMode: 0, InitialMode: 0));
        check(redundant.Receipt.GetProperty("Code").GetString() == "COMPLETED" &&
              redundant.Commands.GetArrayLength() == 0, "redundant mode switch sends zero commands");

        var wrong = await RunAsync(new(TargetMode: 1, InitialMode: 0, WrongModel: true));
        check(wrong.Receipt.GetProperty("Code").GetString() == "IDENTITY_MISMATCH" &&
              wrong.Commands.GetArrayLength() == 0, "wrong identity in mode worker causes zero commands");

        var serviceDown = await RunAsync(new(TargetMode: 1, InitialMode: 0, ServiceDown: true));
        check(serviceDown.Receipt.GetProperty("Code").GetString() == "SERVICE_UNAVAILABLE" &&
              serviceDown.Commands.GetArrayLength() == 0, "service unavailable causes zero commands");

        var failedConfirm = await RunAsync(new(TargetMode: 1, InitialMode: 0, FailConfirm: true));
        check(failedConfirm.Receipt.GetProperty("Code").GetString() == "TARGET_NOT_REACHED" &&
              failedConfirm.Receipt.GetProperty("Recovery").GetString() == "RESTORED_PREVIOUS" &&
              failedConfirm.Commands.GetArrayLength() >= 2, "confirmation timeout attempts rollback to previous mode");

        var fanLockName = "Local\\PCControlCenter.TestFan." + Guid.NewGuid().ToString("N");
        using (var fanMutex = new Mutex(true, fanLockName))
        {
            var busyFan = await RunAsync(new(TargetMode: 1, InitialMode: 0), fanMutex: fanLockName);
            check(busyFan.Receipt.GetProperty("Code").GetString() == "BUSY" &&
                  busyFan.Commands.GetArrayLength() == 0, "active fan session mutex blocks mode switch");
        }

        var modeLockName = "Local\\PCControlCenter.TestMode." + Guid.NewGuid().ToString("N");
        using (var modeMutex = new Mutex(true, modeLockName))
        {
            var busyMode = await RunAsync(new(TargetMode: 1, InitialMode: 0), modeMutex: modeLockName);
            check(busyMode.Receipt.GetProperty("Code").GetString() == "BUSY" &&
                  busyMode.Commands.GetArrayLength() == 0, "concurrent mode session mutex blocks second switch");
        }
    }
}
