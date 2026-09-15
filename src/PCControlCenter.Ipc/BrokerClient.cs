using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using PCControlCenter.Providers.Windows;
namespace PCControlCenter.Ipc;

[SupportedOSPlatform("windows")]
public static class BrokerClient
{
    public static async Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct)
    {
        if (!BrokerProtocol.Valid(request))
            throw new ArgumentException("INVALID_REQUEST");
        var path = Path.Combine(AppContext.BaseDirectory, "pc-control-broker.exe");
        if (!File.Exists(path))
            throw new ProbeException("BROKER_NOT_INSTALLED");
        var name = "pc-control-" + Guid.NewGuid().ToString("N");
        using var pipe = LocalPipe.Create(name);
        var start = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Process? helper;
        ct.ThrowIfCancellationRequested();
        try
        {
            helper = Process.Start(start);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { throw new ProbeException("ELEVATION_CANCELLED"); }
        using var owned = helper ?? throw new ProbeException("BROKER_START_FAILED");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        // The helper is a one-shot process with its own deadline; closing the pipe ends its work.
        var connected = pipe.WaitForConnectionAsync(timeout.Token);
        var exited = owned.WaitForExitAsync(timeout.Token);
        await Task.WhenAny(connected, exited);
        if (!connected.IsCompletedSuccessfully)
        {
            timeout.Cancel();
            try
            {
                await connected;
            }
            catch (OperationCanceledException) { }
            try
            {
                await exited;
            }
            catch (OperationCanceledException) { }
            throw new ProbeException("BROKER_CONNECT_FAILED");
        }
        await connected;
        if (!LocalPipe.ClientIs(pipe, owned.Id))
            throw new ProbeException("BROKER_PEER_MISMATCH");
        await BrokerProtocol.SendAsync(pipe, request, timeout.Token);
        BrokerResponse response;
        try
        {
            response = await BrokerProtocol.ReceiveAsync<BrokerResponse>(pipe, timeout.Token);
        }
        catch (EndOfStreamException) { await owned.WaitForExitAsync(timeout.Token); throw new ProbeException("BROKER_EXIT_" + owned.ExitCode); }
        if (response.Version != BrokerProtocol.Version || response.RequestId != request.RequestId)
            throw new ProbeException("BROKER_PROTOCOL_MISMATCH");
        return response;
    }
}
