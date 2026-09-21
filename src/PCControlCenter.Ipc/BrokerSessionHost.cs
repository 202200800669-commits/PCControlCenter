using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;

namespace PCControlCenter.Ipc;

public static class BrokerSessionHost
{
    public static async Task RunAsync(Stream pipe, BrokerRequest authorization)
    {
        if (authorization.Operation != "authorize" || !BrokerProtocol.ValidSessionRequest(authorization))
            throw new ArgumentException("INVALID_AUTHORIZATION");
        async Task Send(BrokerResponse response)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await BrokerProtocol.SendAsync(pipe, response, timeout.Token);
        }
        await Send(new(BrokerProtocol.Version, authorization.RequestId, "OK"));
        while (true)
        {
            BrokerRequest request;
            try
            {
                using var idle = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                request = await BrokerProtocol.ReceiveAsync<BrokerRequest>(pipe, idle.Token);
            }
            catch { return; }
            if ((!BrokerProtocol.Valid(request) && !(BrokerProtocol.ValidSessionRequest(request) && request.Operation == "keep-alive")) || request.Device != authorization.Device)
                return;
            if (request.Operation == "keep-alive")
            {
                await Send(new(BrokerProtocol.Version, request.RequestId, "OK"));
                continue;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            var probe = new WindowsProbe();
            var actual = await WindowsProbe.IdentifyAsync(probe, deadline.Token);
            if (actual != request.Device || !new ThinkBookProvider(probe).Matches(actual))
            {
                await Send(new(BrokerProtocol.Version, request.RequestId, "IDENTITY_MISMATCH"));
                continue;
            }
            if (request.Operation == "fan-control")
            {
                await FanControlHost.RunAsync(pipe, request);
                continue;
            }
            BrokerResponse response;
            if (request.Operation == "read-fans")
            {
                try
                {
                    var state = await probe.QueryAsync(ProbeKind.ThinkBookFans, deadline.Token);
                    response = new(BrokerProtocol.Version, request.RequestId, "OK", state.GetProperty("fan1").GetInt32(), state.GetProperty("fan2").GetInt32());
                }
                catch { response = new(BrokerProtocol.Version, request.RequestId, "PROBE_FAILED"); }
            }
            else
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                using var watch = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                async Task WatchDisconnect()
                {
                    try
                    {
                        var count = await pipe.ReadAsync(new byte[1], watch.Token);
                        if (count is 0 or 1)
                            stop.Cancel();
                    }
                    catch (OperationCanceledException) { }
                    catch (IOException) { stop.Cancel(); }
                }
                var disconnected = WatchDisconnect();
                try
                {
                    string recovery, code;
                    if (request.Operation == "set-energy")
                    {
                        var receipt = await EnergySessionRunner.RunAsync(request.Energy!, stop.Token, request.RequestId);
                        response = new(BrokerProtocol.Version, request.RequestId, "OK", EnergyReceipt: receipt);
                        recovery = receipt.Recovery;
                        code = receipt.Code;
                    }
                    else if (request.Operation == "set-mode")
                    {
                        var receipt = await ModeSessionRunner.RunAsync(request.Mode!, stop.Token, request.RequestId);
                        response = new(BrokerProtocol.Version, request.RequestId, "OK", ModeReceipt: receipt);
                        recovery = receipt.Recovery;
                        code = receipt.Code;
                    }
                    else
                    {
                        var receipt = await FanSessionRunner.RunAsync(request.Trial!, stop.Token, request.RequestId);
                        response = new(BrokerProtocol.Version, request.RequestId, "OK", Receipt: receipt);
                        recovery = receipt.Recovery;
                        code = receipt.Code;
                    }
                    SessionReceiptStore.WriteReceipt(new(request.RequestId, request.Operation, RecoveryOutcome.TerminalState(recovery, code), recovery, code, TimestampUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                }
                finally { watch.Cancel(); await disconnected; }
            }
            await Send(response);
        }
    }
}
