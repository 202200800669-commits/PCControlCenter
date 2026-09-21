using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;

namespace PCControlCenter.Ipc;

// One elevated, parent-bound helper per desktop run. Only explicit authorization starts it.
[SupportedOSPlatform("windows")]
public static class AuthorizedBrokerSession
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static NamedPipeServerStream? pipe;
    private static Process? helper;
    private static CancellationTokenSource? keepAlive;
    private static DeviceIdentity? identity;
    public static bool IsAuthorized
    {
        get
        {
            try
            {
                return pipe is { IsConnected: true } && helper is { HasExited: false };
            }
            catch (InvalidOperationException) { return false; }
        }
    }
    public static event Action? Changed;

    private static void Close()
    {
        keepAlive?.Cancel();
        keepAlive?.Dispose();
        keepAlive = null;
        pipe?.Dispose();
        pipe = null;
        helper?.Dispose();
        helper = null;
        identity = null;
        Changed?.Invoke();
    }
    public static void Shutdown() => Close();

    public static async Task AuthorizeAsync(DeviceIdentity device, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (IsAuthorized)
                return;
            Close();
            string name = "pc-control-" + Guid.NewGuid().ToString("N");
            pipe = LocalPipe.Create(name);
            string path = Path.Combine(AppContext.BaseDirectory, "pc-control-broker.exe");
            if (!File.Exists(path))
                throw new IOException("BROKER_NOT_INSTALLED");
            var start = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add(name);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            try
            {
                helper = Process.Start(start) ?? throw new IOException("BROKER_START_FAILED");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new ProbeException("ELEVATION_CANCELLED"); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = helper.WaitForExitAsync(timeout.Token);
            await Task.WhenAny(connected, exited);
            if (!connected.IsCompletedSuccessfully)
                throw new IOException("BROKER_CONNECT_FAILED");
            if (!LocalPipe.ClientIs(pipe, helper.Id))
                throw new IOException("BROKER_PEER_MISMATCH");
            var request = new BrokerRequest(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "authorize", device);
            await Exchange(request, timeout.Token);
            identity = device;
            keepAlive = new CancellationTokenSource();
            _ = KeepAliveAsync(keepAlive.Token);
            Changed?.Invoke();
        }
        catch { Close(); throw; }
        finally { Gate.Release(); }
    }
    private static async Task KeepAliveAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(5000, ct);
                if (!await Gate.WaitAsync(0, ct))
                    continue;
                try
                {
                    if (!IsAuthorized || identity is null)
                    {
                        Close();
                        return;
                    }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await Exchange(new(BrokerProtocol.Version, Guid.NewGuid().ToString("N"), "keep-alive", identity), timeout.Token);
                }
                catch { Close(); return; }
                finally { Gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
    }
    private static async Task<BrokerResponse> Exchange(BrokerRequest request, CancellationToken ct)
    {
        await BrokerProtocol.SendAsync(pipe!, request, ct);
        var response = await BrokerProtocol.ReceiveAsync<BrokerResponse>(pipe!, ct);
        if (response.Version != BrokerProtocol.Version || response.RequestId != request.RequestId)
            throw new IOException("BROKER_PROTOCOL_MISMATCH");
        if (request.Operation == "authorize" && response.Code != "OK")
            throw new IOException(response.Code);
        return response;
    }
    public static async Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct)
    {
        if (!BrokerProtocol.Valid(request) || request.Operation == "fan-control")
            throw new ArgumentException("INVALID_REQUEST");
        await Gate.WaitAsync(ct);
        try
        {
            if (!IsAuthorized)
                throw new ProbeException("AUTHORIZATION_REQUIRED");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(100));
            return await Exchange(request, timeout.Token);
        }
        catch { Close(); throw; }
        finally { Gate.Release(); }
    }
    public static async Task<BrokerResponse> RunFanControlAsync(BrokerRequest request, Func<(int First, int Second)> target, Action<BrokerResponse> update, CancellationToken ct)
    {
        if (request.Operation != "fan-control" || !BrokerProtocol.Valid(request))
            throw new ArgumentException("INVALID_REQUEST");
        await Gate.WaitAsync(ct);
        try
        {
            if (!IsAuthorized)
                throw new ProbeException("AUTHORIZATION_REQUIRED");
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var response = await Exchange(request, startup.Token);
            if (response.Code != "RUNNING")
                return response;
            update(response);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(800, ct);
                }
                catch (OperationCanceledException) { break; }
                var rpm = target();
                var pulse = new FanControlUpdate(BrokerProtocol.Version, request.RequestId, rpm.First, rpm.Second);
                if (!pulse.ValidFor(request.RequestId))
                    throw new ArgumentException("INVALID_UPDATE");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await BrokerProtocol.SendAsync(pipe!, pulse, timeout.Token);
                response = await BrokerProtocol.ReceiveAsync<BrokerResponse>(pipe!, timeout.Token);
                if (response.Version != BrokerProtocol.Version || response.RequestId != request.RequestId)
                    throw new IOException("BROKER_PROTOCOL_MISMATCH");
                if (response.Code != "RUNNING")
                    return response;
                update(response);
            }
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await BrokerProtocol.SendAsync(pipe!, new FanControlUpdate(BrokerProtocol.Version, request.RequestId, 0, 0, true), recovery.Token);
            response = await BrokerProtocol.ReceiveAsync<BrokerResponse>(pipe!, recovery.Token);
            if (response.Version != BrokerProtocol.Version || response.RequestId != request.RequestId)
                throw new IOException("BROKER_PROTOCOL_MISMATCH");
            return response;
        }
        catch { Close(); throw; }
        finally { Gate.Release(); }
    }
}
