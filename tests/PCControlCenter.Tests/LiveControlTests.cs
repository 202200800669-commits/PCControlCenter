using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCControlCenter.Core;
using PCControlCenter.Ipc;
using PCControlCenter.Providers.Windows;

static class LiveControlTests
{
    static Process Start(string body, string? directory = null)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.System)
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(body)) })
            start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    static async Task<string> Source(string name)
    {
        using var stream = typeof(FanSessionRunner).Assembly.GetManifestResourceStream("PCControlCenter.Providers.Windows." + name)!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
    public static async Task RunAsync(Action<bool, string> check, DeviceIdentity device)
    {
        string id = Guid.NewGuid().ToString("N");
        var request = new BrokerRequest(BrokerProtocol.Version, id, "fan-control", device, new FanTrial("manual", 4000, 4200, 5));
        check(BrokerProtocol.Valid(request), "live fan session accepts bounded initial target");
        check(!BrokerProtocol.Valid(request with
        {
            Trial = new("full", 0, 0, 5)
        }) && !BrokerProtocol.Valid(request with
        {
            Trial = new("manual", 0, 7000, 5)
        }), "live fan session rejects alternate modes and unsafe targets");
        check(new FanControlUpdate(2, id, 3500, 5500).ValidFor(id) && new FanControlUpdate(2, id, 0, 0, true).ValidFor(id), "live pulse and explicit stop are distinct valid messages");
        check(!new FanControlUpdate(2, "other", 3500, 4500).ValidFor(id) && !new FanControlUpdate(2, id, 3500, 4500, true).ValidFor(id) && !new FanControlUpdate(1, id, 3500, 4500).ValidFor(id), "live protocol rejects other session, disguised stop and wrong version");
        var auth = new BrokerRequest(2, id, "authorize", device);
        check(BrokerProtocol.ValidSessionRequest(auth) && !BrokerProtocol.ValidSessionRequest(auth with
        {
            Trial = request.Trial
        }), "authorization never carries a hardware command");
        string name = "pc-control-test-" + Guid.NewGuid().ToString("N");
        using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            var connect = server.WaitForConnectionAsync();
            await client.ConnectAsync();
            await connect;
            var host = BrokerSessionHost.RunAsync(server, auth);
            var ready = await BrokerProtocol.ReceiveAsync<BrokerResponse>(client, default);
            bool reusable = ready.Code == "OK";
            for (int i = 0; i < 3; i++)
            {
                var ping = auth with
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Operation = "keep-alive"
                };
                await BrokerProtocol.SendAsync(client, ping, default);
                var response = await BrokerProtocol.ReceiveAsync<BrokerResponse>(client, default);
                reusable &= response.Code == "OK" && response.RequestId == ping.RequestId;
            }
            check(reusable, "one authorization connection serves repeated requests without launching another helper");
            await BrokerProtocol.SendAsync(client, auth with
            {
                Operation = "keep-alive",
                Device = device with
                {
                    Bios = "changed"
                }
            }, default);
            await host.WaitAsync(TimeSpan.FromSeconds(3));
            check(host.IsCompletedSuccessfully, "authorized session rejects changed identity before any hardware call");
        }
        // The former self-contained-package failure: System.dll in cwd is a .NET Core facade.
        string temp = Path.Combine(Path.GetTempPath(), "pc-energy-compile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.Copy(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.dll"), Path.Combine(temp, "System.dll"));
            string energy = await Source("EnergySession.ps1");
            string csharp = Regex.Match(energy, "(?s)\\$csharp = @'\\r?\\n(.*?)\\r?\\n'@").Groups[1].Value;
            string addType = energy.Split('\n').Single(x => x.TrimStart().StartsWith("Add-Type -TypeDefinition"));
            string body = "$ErrorActionPreference='Stop';$csharp=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(csharp)) + "'));" + addType + ";'COMPILED'";
            using var compiler = Start(body, temp);
            var err = compiler.StandardError.ReadToEndAsync();
            var output = compiler.StandardOutput.ReadToEndAsync();
            await compiler.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            check(compiler.ExitCode == 0 && (await output).Contains("COMPILED"), "real EnergyBridge compiles even beside published .NET Core System.dll");
            await err;
        }
        finally { Directory.Delete(temp, true); }

        await Worker(check, "disconnect", keepSeconds: 32);
        await Worker(check, "timeout");
        await Worker(check, "invalid");
        await Worker(check, "wrong-model");
        await Host(check, request, "stop");
        await Host(check, request, "disconnect");
        await Host(check, request, "invalid");
    }
    static async Task Host(Action<bool, string> check, BrokerRequest request, string scenario)
    {
        string name = "pc-live-host-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connect = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await connect;
        SessionReceiptRecord? saved = null;
        Process StartMock(ProcessStartInfo start)
        {
            string script = Encoding.Unicode.GetString(Convert.FromBase64String(start.ArgumentList[^1]));
            const string mutex = @"Global\PCControlCenter.ThinkBookFanSession";
            if (!script.Contains(mutex))
                throw new Exception("Fan host test isolation needs review");
            script = script.Replace(mutex, "Local\\PCControlCenter.TestHost." + Guid.NewGuid().ToString("N"));
            start.ArgumentList[^1] = Convert.ToBase64String(Encoding.Unicode.GetBytes(FanWorkerTests.MockHardware + "\n" + script));
            return Process.Start(start)!;
        }
        var host = FanControlHost.RunAsync(server, request, StartMock, r => saved = r);
        var ready = await BrokerProtocol.ReceiveAsync<BrokerResponse>(client, default).WaitAsync(TimeSpan.FromSeconds(8));
        check(ready.Code == "RUNNING", "broker live host acknowledges worker readiness");
        await BrokerProtocol.SendAsync(client, new FanControlUpdate(2, request.RequestId, 4700, 4900), default);
        var changed = await BrokerProtocol.ReceiveAsync<BrokerResponse>(client, default).WaitAsync(TimeSpan.FromSeconds(5));
        check(changed.Fan1 == 4700 && changed.Fan2 == 4900, "broker live host returns independent updated readings");
        if (scenario == "disconnect")
            client.Dispose();
        else
        {
            var stop = scenario == "stop" ? new FanControlUpdate(2, request.RequestId, 0, 0, true) : new FanControlUpdate(2, "wrong-session", 2000, 2000);
            await BrokerProtocol.SendAsync(client, stop, default);
            var final = await BrokerProtocol.ReceiveAsync<BrokerResponse>(client, default).WaitAsync(TimeSpan.FromSeconds(8));
            check(final.Receipt?.Recovery == "AUTO_COMMANDS_SENT_OVERRIDE_OFF", "broker host sends final recovery acknowledgement on " + scenario);
        }
        await host.WaitAsync(TimeSpan.FromSeconds(10));
        check(saved?.Recovery == "AUTO_COMMANDS_SENT_OVERRIDE_OFF" && saved.State == "Completed", "broker persists actual final receipt on " + scenario);
        if (scenario == "stop")
        {
            var next = BrokerProtocol.SendAsync(server, new BrokerResponse(2, request.RequestId, "NEXT"), default);
            check((await BrokerProtocol.ReceiveAsync<BrokerResponse>(client, default)).Code == "NEXT", "live stop leaves stream aligned for the next authorized operation");
            await next;
        }
    }
    static async Task Worker(Action<bool, string> check, string scenario, int keepSeconds = 0)
    {
        string script = await Source("FanSession.ps1");
        const string mutex = @"Global\PCControlCenter.ThinkBookFanSession";
        if (!script.Contains(mutex))
            throw new Exception("Worker mutex isolation needs review");
        script = script.Replace(mutex, "Local\\PCControlCenter.TestLive." + Guid.NewGuid().ToString("N"));
        if (scenario == "timeout")
            script = script.Replace("$leaseSeconds=10", "$leaseSeconds=1");
        string prefix = "$continuous=$true;$mode='manual';$rpm1=4000;$rpm2=4200;$seconds=5;$failAt=0;$signal=$false;$wrongModel=$" + (scenario == "wrong-model" ? "true" : "false") + ";\n";
        using var process = Start(prefix + FanWorkerTests.MockHardware + "\n" + script + "\nConvertTo-Json -InputObject @($script:writes.ToArray()) -Compress");
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            string? first = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (scenario == "wrong-model")
            {
                string? writes = await process.StandardOutput.ReadLineAsync();
                await process.WaitForExitAsync();
                check(JsonDocument.Parse(writes!).RootElement.GetArrayLength() == 0, "live worker rejects wrong hardware identity with zero writes");
                return;
            }
            check(JsonDocument.Parse(first!).RootElement.GetProperty("Code").GetString() == "RUNNING", "live worker starts with an acknowledged target");
            if (keepSeconds > 0)
            {
                var time = Stopwatch.StartNew();
                int count = 0;
                while (time.Elapsed.TotalSeconds < keepSeconds)
                {
                    await process.StandardInput.WriteLineAsync(count++ % 2 == 0 ? "4500,4700" : "4000,4200");
                    await process.StandardInput.FlushAsync();
                    var state = JsonDocument.Parse((await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!).RootElement;
                    if (state.GetProperty("Code").GetString() != "RUNNING")
                        throw new Exception("Live worker stopped unexpectedly");
                    await Task.Delay(800);
                }
                check(!process.HasExited, "continuous manual targets remain adjustable beyond the former 30-second limit");
                process.StandardInput.Close();
            }
            else if (scenario == "invalid")
            {
                await process.StandardInput.WriteLineAsync("0,9999");
                await process.StandardInput.FlushAsync();
            }
            var final = JsonDocument.Parse((await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(12)))!).RootElement;
            var writesDoc = JsonDocument.Parse((await process.StandardOutput.ReadLineAsync())!);
            await process.WaitForExitAsync();
            check(final.GetProperty("Recovery").GetString() == "AUTO_COMMANDS_SENT_OVERRIDE_OFF" && writesDoc.RootElement.EnumerateArray().TakeLast(3).All(x => x.GetProperty("value").GetInt32() == 0), "live " + scenario + " restores all automatic channels");
            if (scenario == "invalid")
                check(writesDoc.RootElement.GetArrayLength() == 6, "malformed live target never reaches hardware writes");
            if (scenario == "timeout")
                check(final.GetProperty("Code").GetString() == "HEARTBEAT_EXPIRED", "lost heartbeat independently expires in the worker");
            if (process.ExitCode != 0)
                throw new Exception(await errors);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
