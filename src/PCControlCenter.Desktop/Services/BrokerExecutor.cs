using System;
using System.Threading;
using System.Threading.Tasks;
using PCControlCenter.Ipc;

namespace PCControlCenter.Desktop.Services;

public interface IBrokerExecutor
{
    Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct);
    Task<bool> WaitForRecoveryCompleteAsync(CancellationToken ct = default);
}

public sealed class RealBrokerExecutor : IBrokerExecutor
{
    public Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct) =>
        BrokerClient.ExecuteAsync(request, ct);

    public async Task<bool> WaitForRecoveryCompleteAsync(CancellationToken ct = default)
    {
        for (int i = 0; i < 75; i++)
        {
            if (ct.IsCancellationRequested)
                return false;
            try
            {
                using var m = Mutex.OpenExisting(@"Global\PCControlCenter.ThinkBookFanSession");
                if (m.WaitOne(0))
                {
                    m.ReleaseMutex();
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return true;
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
            catch
            {
            }
            await Task.Delay(200, ct);
        }
        return false;
    }
}
