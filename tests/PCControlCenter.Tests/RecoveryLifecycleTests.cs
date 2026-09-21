using System.Diagnostics;
using System.Text;
using PCControlCenter.Core;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Ipc;
using PCControlCenter.Providers.Windows;

static class RecoveryLifecycleTests
{
    public static async Task RunHostAsync(string[] args)
    {
        var directory = args[1];
        var kind = args[2];
        var id = args[3];
        string Literal(string s) => "'" + s.Replace("'", "''") + "'";
        var json = kind switch
        {
            "mode" => "{\"Code\":\"INTERRUPTED\",\"TargetMode\":1,\"PreviousMode\":0,\"FinalMode\":0,\"Recovery\":\"RESTORED_PREVIOUS\"}",
            "energy" => "{\"Code\":\"INTERRUPTED\",\"Kind\":\"charge\",\"TargetValue\":1,\"PreviousValue\":0,\"FinalValue\":0,\"Recovery\":\"RESTORED_PREVIOUS\"}",
            _ => "{\"Code\":\"INTERRUPTED\",\"Recovery\":\"AUTO_COMMANDS_SENT_OVERRIDE_OFF\"}"
        };
        var script = "[IO.File]::WriteAllText(" + Literal(Path.Combine(directory, "ready")) + ",[string]$PID);" +
            "while(!(Test-Path -LiteralPath " + Literal(Path.Combine(directory, "release")) + ")){Start-Sleep -Milliseconds 50};" +
            "[Console]::WriteLine(" + Literal(json) + ")";
        Func<ProcessStartInfo, Process?> fake = _ =>
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(arg);
            return new Process { StartInfo = start };
        };
        ModeSessionRunner.ProcessLauncher = EnergySessionRunner.ProcessLauncher = FanSessionRunner.ProcessLauncher = fake;
        ModeSessionRunner.TargetPhaseTimeout = EnergySessionRunner.TargetPhaseTimeout = TimeSpan.FromMilliseconds(30);
        ModeSessionRunner.RecoveryWaitTimeout = EnergySessionRunner.RecoveryWaitTimeout = FanSessionRunner.RecoveryWaitTimeout = TimeSpan.FromMilliseconds(30);
        string code, recovery;
        if (kind == "mode")
        {
            var r = await ModeSessionRunner.RunAsync(new ModeRequest(1), CancellationToken.None, id);
            code = r.Code;
            recovery = r.Recovery;
        }
        else if (kind == "energy")
        {
            var r = await EnergySessionRunner.RunAsync(new EnergyRequest("charge", 1), CancellationToken.None, id);
            code = r.Code;
            recovery = r.Recovery;
        }
        else
        {
            var r = await FanSessionRunner.RunAsync(new FanTrial("full", 0, 0, 5), CancellationToken.None, id);
            code = r.Code;
            recovery = r.Recovery;
        }
        SessionReceiptStore.WriteReceipt(new(id, kind, RecoveryOutcome.TerminalState(recovery, code), recovery, code), directory);
    }

    public static async Task RunAllAsync(Action<bool, string> check)
    {
        foreach (var failed in new[] { "ROLLBACK_FAILED", "UNCONFIRMED", "unknown", null })
            check(!RecoveryOutcome.IsConfirmed(failed, "CONTROL_FAILED"), "unconfirmed/failed recovery is never classified as completed");
        var requestId = Guid.NewGuid().ToString("N");
        var calls = 0;
        var reader = new RealBrokerExecutor(id => ++calls < 3 ? null : new(id, "fan-trial", "Completed", "AUTO_COMMANDS_SENT_OVERRIDE_OFF", "INTERRUPTED"), TimeSpan.FromSeconds(2));
        var late = await reader.WaitForRecoveryAsync(requestId);
        check(calls >= 3 && late.State == SessionState.RecoveryCompleted, "receipt polling waits for a late result without inferring state from missing locks");
        var failedReader = new RealBrokerExecutor(id => new(id, "set-mode", "Completed", "ROLLBACK_FAILED", "CONTROL_FAILED"));
        check((await failedReader.WaitForRecoveryAsync(requestId)).State == SessionState.TerminatedUnconfirmed, "legacy completed record containing rollback failure remains unconfirmed");
        var missingReader = new RealBrokerExecutor(_ => null, TimeSpan.FromMilliseconds(30));
        check((await missingReader.WaitForRecoveryAsync(requestId)).State == SessionState.TimedOutUnconfirmed, "missing receipt produces unconfirmed timeout without exception");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        check((await missingReader.WaitForRecoveryAsync(requestId, cancelled.Token)).State == SessionState.TimedOutUnconfirmed, "cancelled receipt wait returns explicit unconfirmed result");
        if (!OperatingSystem.IsWindows())
            return;
        foreach (var kind in new[] { "mode", "energy", "fan" })
        {
            var directory = Path.Combine(Path.GetTempPath(), "pc-control-recovery-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid().ToString("N");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(RecoveryLifecycleTests).Assembly.Location);
            foreach (var arg in new[] { "--recovery-test-host", directory, kind, id })
                start.ArgumentList.Add(arg);
            using var host = Process.Start(start)!;
            int? workerPid = null;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!File.Exists(Path.Combine(directory, "ready")))
                {
                    if (host.HasExited)
                        throw new Exception("Recovery host exited before worker startup");
                    await Task.Delay(30, timeout.Token);
                }
                workerPid = int.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "ready"), timeout.Token));
                while (!RecoveryProcessTracker.HasInFlightRecovery)
                    await Task.Delay(30, timeout.Token);
                await Task.Delay(200, timeout.Token);
                check(!host.HasExited, kind + " owner stays alive beyond request/recovery wait windows");
                check(RecoveryProcessTracker.HasInFlightRecovery, kind + " recovery is visible in a second process");
                check(SessionReceiptStore.GetReceipt(id, directory) is null, kind + " does not persist a premature final timeout receipt");
                await File.WriteAllTextAsync(Path.Combine(directory, "release"), "release", timeout.Token);
                await host.WaitForExitAsync(timeout.Token);
                var receipt = SessionReceiptStore.GetReceipt(id, directory);
                check(host.ExitCode == 0 && receipt?.State == "Completed" && receipt.Code == "INTERRUPTED", kind + " owner persists the actual late worker receipt before exiting");
                check(!RecoveryProcessTracker.HasInFlightRecovery, kind + " completion clears the cross-process recovery marker");
            }
            finally
            {
                if (!host.HasExited)
                {
                    host.Kill(true);
                    await host.WaitForExitAsync();
                }
                if (workerPid.HasValue)
                {
                    try
                    {
                        using var worker = Process.GetProcessById(workerPid.Value);
                        if (!worker.HasExited)
                        {
                            worker.Kill(true);
                            await worker.WaitForExitAsync();
                        }
                    }
                    catch (ArgumentException) { }
                }
                // Only remove this test's verified temporary child directory.
                if (Path.GetDirectoryName(Path.GetFullPath(directory)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                    throw new InvalidOperationException("Unexpected test directory");
                Directory.Delete(directory, true);
            }
        }
    }
}
