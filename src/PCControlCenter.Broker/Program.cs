using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.RegularExpressions;
using PCControlCenter.Ipc;
using PCControlCenter.Providers.Windows;
if (!OperatingSystem.IsWindows()) return 2;
if (args.Length != 2 || !Regex.IsMatch(args[0], "^pc-control-[0-9a-f]{32}$") || !int.TryParse(args[1], out int parent) || parent <= 0) return 2;
if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
int stage = 10;
try
{
    using var parentProcess = Process.GetProcessById(parent);
    var caller = parentProcess.MainModule?.FileName;
    var expectedCli = Path.Combine(AppContext.BaseDirectory, "pc-control.exe");
    var expectedDesktop = Path.Combine(AppContext.BaseDirectory, "pc-control-desktop.exe");
    if (!string.Equals(caller, expectedCli, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(caller, expectedDesktop, StringComparison.OrdinalIgnoreCase))
        return 4;
    using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
    await pipe.ConnectAsync(deadline.Token);
    if (!LocalPipe.ServerIs(pipe, parent))
        return 4;
    stage = 20;
    var request = await BrokerProtocol.ReceiveAsync<BrokerRequest>(pipe, deadline.Token);
    if (!BrokerProtocol.Valid(request))
        return 5;
    stage = 30;
    BrokerResponse response;
    try
    {
        var probe = new WindowsProbe();
        var actual = await WindowsProbe.IdentifyAsync(probe, deadline.Token);
        if (actual != request.Device || !new ThinkBookProvider(probe).Matches(actual))
            response = new(BrokerProtocol.Version, request.RequestId, "IDENTITY_MISMATCH");
        else
        {
            if (request.Operation == "read-fans")
            {
                var reading = await probe.QueryAsync(ProbeKind.ThinkBookFans, deadline.Token);
                response = new(BrokerProtocol.Version, request.RequestId, "OK", reading.GetProperty("fan1").GetInt32(), reading.GetProperty("fan2").GetInt32());
            }
            else if (request.Operation == "fan-trial")
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                using var watch = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                async Task WatchDisconnect()
                {
                    try
                    {
                        var buffer = new byte[1];
                        var count = await pipe.ReadAsync(buffer, watch.Token);
                        if (count is 0 or 1)
                            stop.Cancel();
                    }
                    catch (OperationCanceledException) { }
                    catch (IOException) { stop.Cancel(); }
                }
                var disconnected = WatchDisconnect();
                var receipt = await FanSessionRunner.RunAsync(request.Trial!, stop.Token);
                watch.Cancel();
                await disconnected;
                response = new(BrokerProtocol.Version, request.RequestId, "OK", Receipt: receipt);
            }
            else if (request.Operation == "set-mode")
            {
                var receipt = await ModeSessionRunner.RunAsync(request.Mode!, deadline.Token);
                response = new(BrokerProtocol.Version, request.RequestId, "OK", ModeReceipt: receipt);
            }
            else if (request.Operation == "set-energy")
            {
                var receipt = await EnergySessionRunner.RunAsync(request.Energy!, deadline.Token);
                response = new(BrokerProtocol.Version, request.RequestId, "OK", EnergyReceipt: receipt);
            }
            else
            {
                response = new(BrokerProtocol.Version, request.RequestId, "UNKNOWN_OPERATION");
            }
        }
    }
    catch (ProbeException e) { response = new(BrokerProtocol.Version, request.RequestId, e.Code == "ACCESS_DENIED" ? "ACCESS_DENIED" : "PROBE_FAILED"); }
    catch (Exception) { response = new(BrokerProtocol.Version, request.RequestId, "BROKER_READ_FAILED"); }
    stage = 40;
    await BrokerProtocol.SendAsync(pipe, response, deadline.Token);
    return 0;
}
catch (Exception) { return stage + 100; }
