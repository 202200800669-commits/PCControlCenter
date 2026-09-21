using System;
using System.Threading;
using System.Threading.Tasks;
using PCControlCenter.Ipc;

namespace PCControlCenter.Desktop.Services;

public interface IManualFanSession
{
    bool IsAuthorized
    {
        get;
    }
    Task<BrokerResponse> RunAsync(BrokerRequest request, Func<(int First, int Second)> target, Action<BrokerResponse> update, CancellationToken ct);
}

public sealed class ManualFanSession : IManualFanSession
{
    public bool IsAuthorized => AuthorizedBrokerSession.IsAuthorized;
    public Task<BrokerResponse> RunAsync(BrokerRequest request, Func<(int First, int Second)> target, Action<BrokerResponse> update, CancellationToken ct) =>
        AuthorizedBrokerSession.RunFanControlAsync(request, target, update, ct);
}
