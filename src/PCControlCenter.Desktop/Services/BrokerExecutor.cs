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
    public async Task<BrokerResponse> ExecuteAsync(BrokerRequest request, CancellationToken ct)
    {
        BrokerSessionTracker.RecordStart(request.RequestId);
        try
        {
            var resp = await BrokerClient.ExecuteAsync(request, ct);
            var recovery = resp.Receipt?.Recovery ?? resp.ModeReceipt?.Recovery ?? resp.EnergyReceipt?.Recovery;
            BrokerSessionTracker.RecordState(request.RequestId, SessionState.RecoveryCompleted, recovery, resp.Code);
            return resp;
        }
        catch (OperationCanceledException)
        {
            BrokerSessionTracker.RecordState(request.RequestId, SessionState.Recovering);
            throw;
        }
        catch (Exception ex)
        {
            BrokerSessionTracker.RecordState(request.RequestId, SessionState.TerminatedUnconfirmed, "UNCONFIRMED", ex.Message);
            throw;
        }
    }

    public async Task<SessionStatus> WaitForRecoveryAsync(string requestId, CancellationToken ct = default)
    {
        try
        {
            for (int i = 0; i < 75; i++)
            {
                if (ct.IsCancellationRequested)
                    break;

                var current = BrokerSessionTracker.GetStatus(requestId);
                if (current.State is SessionState.RecoveryCompleted or SessionState.TerminatedUnconfirmed)
                    return current;

                try
                {
                    using var m = Mutex.OpenExisting(@"Global\PCControlCenter.ThinkBookFanSession");
                    bool acquired = false;
                    try
                    {
                        acquired = m.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        // 异常退出：工作进程异常崩溃或被杀死，互斥锁遗弃。
                        // 必须释放取得的所有权并标记为异常未确认！
                        try
                        {
                            m.ReleaseMutex();
                        }
                        catch { }
                        var term = new SessionStatus(requestId, SessionState.TerminatedUnconfirmed, "UNCONFIRMED", "底层工作进程异常退出，控制锁被遗弃");
                        BrokerSessionTracker.Update(term);
                        return term;
                    }

                    if (acquired)
                    {
                        try
                        {
                            m.ReleaseMutex();
                        }
                        catch { }
                        var s = BrokerSessionTracker.GetStatus(requestId);
                        if (s.State == SessionState.NotStarted)
                        {
                            return new SessionStatus(requestId, SessionState.NotStarted, "NOT_NEEDED", "操作在创建底层锁前已取消");
                        }
                        var done = new SessionStatus(requestId, SessionState.RecoveryCompleted, s.Recovery ?? "NOT_NEEDED", "底层控制锁已释放");
                        BrokerSessionTracker.Update(done);
                        return done;
                    }
                    else
                    {
                        BrokerSessionTracker.RecordState(requestId, SessionState.Recovering);
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    var s = BrokerSessionTracker.GetStatus(requestId);
                    if (s.State == SessionState.NotStarted)
                    {
                        return new SessionStatus(requestId, SessionState.NotStarted, "NOT_NEEDED", "操作在创建底层锁前已取消");
                    }
                    if (s.State is SessionState.Running or SessionState.Recovering)
                    {
                        var unconf = new SessionStatus(requestId, SessionState.TerminatedUnconfirmed, "UNCONFIRMED", "底层工作进程退出但未取得恢复回执");
                        BrokerSessionTracker.Update(unconf);
                        return unconf;
                    }
                }
                catch (Exception ex)
                {
                    var err = new SessionStatus(requestId, SessionState.TerminatedUnconfirmed, "UNCONFIRMED", ex.Message);
                    BrokerSessionTracker.Update(err);
                    return err;
                }

                try
                {
                    await Task.Delay(200, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch
        {
        }

        var timeoutStatus = new SessionStatus(requestId, SessionState.TimedOutUnconfirmed, "UNCONFIRMED", "等待底层恢复超时");
        BrokerSessionTracker.Update(timeoutStatus);
        return timeoutStatus;
    }

    public async Task<bool> WaitForRecoveryCompleteAsync(CancellationToken ct = default)
    {
        var status = await WaitForRecoveryAsync("", ct);
        return status.State is SessionState.RecoveryCompleted or SessionState.NotStarted;
    }
}
