using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PCControlCenter.Ipc;

namespace PCControlCenter.Desktop.Services;

public enum SessionState
{
    NotStarted,
    Running,
    Recovering,
    TerminatedUnconfirmed,
    RecoveryCompleted,
    TimedOutUnconfirmed
}

public sealed record SessionStatus(
    string RequestId,
    SessionState State,
    string? Recovery = null,
    string? Message = null);

public static class BrokerSessionTracker
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SessionStatus> Sessions = new();

    public static void RecordStart(string requestId)
    {
        lock (Gate)
        {
            Sessions[requestId] = new SessionStatus(requestId, SessionState.Running);
        }
    }

    public static void RecordState(string requestId, SessionState state, string? recovery = null, string? message = null)
    {
        lock (Gate)
        {
            Sessions[requestId] = new SessionStatus(requestId, state, recovery, message);
        }
    }

    public static void Update(SessionStatus status)
    {
        lock (Gate)
        {
            Sessions[status.RequestId] = status;
        }
    }

    public static SessionStatus GetStatus(string requestId)
    {
        lock (Gate)
        {
            if (Sessions.TryGetValue(requestId, out var s))
                return s;
            return new SessionStatus(requestId, SessionState.NotStarted);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Sessions.Clear();
        }
    }
}

public interface IBrokerExecutor
{
    Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct);
    async Task<SessionStatus> WaitForRecoveryAsync(string requestId, CancellationToken ct = default)
    {
        try
        {
            bool ok = await WaitForRecoveryCompleteAsync(ct);
            return ok
                ? new SessionStatus(requestId, SessionState.RecoveryCompleted, "UNCONFIRMED")
                : new SessionStatus(requestId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED");
        }
        catch (Exception ex)
        {
            return new SessionStatus(requestId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED", ex.Message);
        }
    }
    Task<bool> WaitForRecoveryCompleteAsync(CancellationToken ct = default);
}

public sealed class RealBrokerExecutor : IBrokerExecutor
{
    private readonly Func<string, SessionReceiptRecord?> readReceipt;
    private readonly TimeSpan waitTimeout;

    public RealBrokerExecutor(Func<string, SessionReceiptRecord?>? readReceipt = null, TimeSpan? waitTimeout = null)
    {
        this.readReceipt = readReceipt ?? (id => SessionReceiptStore.GetReceipt(id));
        this.waitTimeout = waitTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct)
    {
        BrokerSessionTracker.RecordStart(request.RequestId);
        try
        {
            var resp = await BrokerClient.ExecuteAsync(request, ct);
            var recovery = resp.Receipt?.Recovery ?? resp.ModeReceipt?.Recovery ?? resp.EnergyReceipt?.Recovery;
            var code = resp.Receipt?.Code ?? resp.ModeReceipt?.Code ?? resp.EnergyReceipt?.Code;
            var state = resp.Code == "OK" && RecoveryOutcome.IsConfirmed(recovery, code)
                ? SessionState.RecoveryCompleted : SessionState.TerminatedUnconfirmed;
            BrokerSessionTracker.RecordState(request.RequestId, state, recovery ?? "UNCONFIRMED", code ?? resp.Code);
            return resp;
        }
        catch (OperationCanceledException)
        {
            BrokerSessionTracker.RecordState(request.RequestId, SessionState.Recovering);
            throw;
        }
        catch (Exception ex)
        {
            // A disconnected client does not establish that the hardware worker has stopped.
            BrokerSessionTracker.RecordState(request.RequestId, SessionState.Recovering, "UNCONFIRMED", ex.Message);
            throw;
        }
    }

    public async Task<SessionStatus> WaitForRecoveryAsync(string requestId, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(waitTimeout);
        try
        {
            while (!deadline.IsCancellationRequested)
            {
                var receipt = readReceipt(requestId);
                if (receipt is not null && receipt.RequestId == requestId &&
                    receipt.State is "Completed" or "TerminatedUnconfirmed")
                {
                    var confirmed = receipt.State == "Completed" && RecoveryOutcome.IsConfirmed(receipt.Recovery, receipt.Code);
                    var state = confirmed ? SessionState.RecoveryCompleted : SessionState.TerminatedUnconfirmed;
                    if (confirmed && receipt.Code == "CANCELLED_BEFORE_WRITE")
                        state = SessionState.NotStarted;
                    var terminal = new SessionStatus(requestId, state, receipt.Recovery ?? "UNCONFIRMED", receipt.Message ?? receipt.Code);
                    BrokerSessionTracker.Update(terminal);
                    return terminal;
                }
                // Missing/free/abandoned global locks say nothing about this request's final receipt.
                // Keep polling so the worker can publish a result after releasing its hardware lock.
                await Task.Delay(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var error = new SessionStatus(requestId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED", ex.Message);
            BrokerSessionTracker.Update(error);
            return error;
        }
        var timeout = new SessionStatus(requestId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED", "恢复等待超时，仍可继续查询此请求的最终回执");
        BrokerSessionTracker.Update(timeout);
        return timeout;
    }

    public async Task<bool> WaitForRecoveryCompleteAsync(CancellationToken ct = default)
    {
        var status = await WaitForRecoveryAsync("", ct);
        return status.State is SessionState.RecoveryCompleted or SessionState.NotStarted;
    }
}
