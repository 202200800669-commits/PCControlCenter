using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;

namespace PCControlCenter.Ipc;

// Runs only after the elevated broker has authenticated its parent and checked the exact device identity.
public static class FanControlHost
{
    public static async Task RunAsync(Stream pipe, BrokerRequest request, Func<ProcessStartInfo, Process>? startWorker = null, Action<SessionReceiptRecord>? persistReceipt = null)
    {
        if (request.Operation != "fan-control" || !BrokerProtocol.Valid(request))
            throw new ArgumentException("INVALID_REQUEST");
        if (RecoveryProcessTracker.HasInFlightRecovery)
        {
            await BrokerProtocol.SendAsync(pipe, new BrokerResponse(BrokerProtocol.Version, request.RequestId, "BUSY"), CancellationToken.None);
            return;
        }
        using var source = typeof(FanSessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows.FanSession.ps1")!;
        using var reader = new StreamReader(source);
        var trial = request.Trial!;
        string script = string.Create(CultureInfo.InvariantCulture, $"$continuous=$true;$mode='manual';$rpm1={trial.Rpm1};$rpm2={trial.Rpm2};$seconds=5;\n") + await reader.ReadToEndAsync();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(arg);
        using var worker = (startWorker is null ? Process.Start(start) : startWorker(start)) ?? throw new IOException("WORKER_START_FAILED");
        var errors = worker.StandardError.ReadToEndAsync();
        FanReceipt? final = null;
        Task<string?>? pendingLine = null;
        async Task<FanReceipt> ReadState()
        {
            pendingLine ??= worker.StandardOutput.ReadLineAsync();
            string? line = await pendingLine.WaitAsync(TimeSpan.FromSeconds(20));
            pendingLine = null;
            if (line is null || line.Length > BrokerProtocol.MaxFrame)
                throw new IOException("WORKER_FAILED");
            return JsonSerializer.Deserialize<FanReceipt>(line) ?? throw new IOException("INVALID_RECEIPT");
        }
        async Task Send(FanReceipt state)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await BrokerProtocol.SendAsync(pipe, new BrokerResponse(BrokerProtocol.Version, request.RequestId,
                state.Code == "RUNNING" ? "RUNNING" : "OK", state.ObservedFan1, state.ObservedFan2, state), timeout.Token);
        }
        try
        {
            var state = await ReadState();
            if (state.Code != "RUNNING")
            {
                final = state;
                return;
            }
            await Send(state);
            while (true)
            {
                using var pulseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var update = await BrokerProtocol.ReceiveAsync<FanControlUpdate>(pipe, pulseTimeout.Token);
                if (!update.ValidFor(request.RequestId))
                    throw new InvalidDataException("INVALID_UPDATE");
                if (update.Stop)
                    break;
                await worker.StandardInput.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{update.Rpm1},{update.Rpm2}"));
                await worker.StandardInput.FlushAsync();
                state = await ReadState();
                if (state.Code != "RUNNING")
                {
                    final = state;
                    break;
                }
                await Send(state);
            }
        }
        catch (Exception) { /* Disconnect, timeout or malformed input all close the worker input and restore. */ }
        finally
        {
            try
            {
                worker.StandardInput.Close();
            }
            catch { }
            // Never kill a hardware worker in the middle of its recovery block.
            var drain = Task.Run(async () =>
            {
                if (pendingLine is not null)
                {
                    var line = await pendingLine;
                    pendingLine = null;
                    if (line is not null && line.Length <= BrokerProtocol.MaxFrame)
                        try
                        {
                            var receipt = JsonSerializer.Deserialize<FanReceipt>(line);
                            if (receipt?.Code != "RUNNING")
                                final = receipt;
                        }
                        catch (JsonException) { }
                }
                while (await worker.StandardOutput.ReadLineAsync() is { } line)
                    if (line.Length <= BrokerProtocol.MaxFrame)
                        try
                        {
                            var receipt = JsonSerializer.Deserialize<FanReceipt>(line);
                            if (receipt?.Code != "RUNNING")
                                final = receipt;
                        }
                        catch (JsonException) { }
            });
            var exited = worker.WaitForExitAsync();
            if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(20))) != exited)
                await RecoveryProcessTracker.WaitForCompletionAsync(worker, request.RequestId);
            else
                await exited;
            await drain;
            await errors;
            final ??= new FanReceipt("WORKER_FAILED", "UNCONFIRMED");
            var record = new SessionReceiptRecord(request.RequestId, request.Operation,
                RecoveryOutcome.TerminalState(final.Recovery, final.Code), final.Recovery, final.Code,
                TimestampUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (persistReceipt is null)
                SessionReceiptStore.WriteReceipt(record);
            else
                persistReceipt(record);
            try
            {
                await Send(final);
            }
            catch { }
        }
    }
}
