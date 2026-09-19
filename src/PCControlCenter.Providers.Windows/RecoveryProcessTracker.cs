using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlCenter.Providers.Windows;

public static class RecoveryProcessTracker
{
    private static readonly object Gate = new();

    private sealed class TrackedItem
    {
        public Process? Process
        {
            get; set;
        }
        public int Pid
        {
            get; init;
        }
        public string RequestId { get; init; } = "";
        public string? MutexName
        {
            get; init;
        }
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly List<TrackedItem> TrackedItems = new();
    private static Mutex? inFlightGlobalMutex;

    public static void Track(Process process, params string[] mutexNames) =>
        Track(process, "", mutexNames.Length > 0 ? mutexNames[0] : null);

    public static void Track(Process process, string requestId, string? mutexName = null)
    {
        int pid;
        try
        {
            pid = process.Id;
        }
        catch
        {
            return;
        }

        var item = new TrackedItem
        {
            Process = process,
            Pid = pid,
            RequestId = requestId,
            MutexName = mutexName
        };

        lock (Gate)
        {
            TrackedItems.Add(item);
            UpdateGlobalRecoveryMutex();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                try
                {
                    await process.WaitForExitAsync();
                }
                catch (Exception)
                {
                    // 应对外部对传入 process 实例意外调用 Dispose 的防御逻辑：回退至基于 PID 的存活探测
                }

                while (true)
                {
                    try
                    {
                        using var live = Process.GetProcessById(pid);
                        if (live.HasExited)
                            break;
                    }
                    catch (ArgumentException)
                    {
                        // 进程在操作系统中已不存在
                        break;
                    }
                    catch
                    {
                        break;
                    }
                    await Task.Delay(100);
                }
            }
            finally
            {
                lock (Gate)
                {
                    TrackedItems.Remove(item);
                    UpdateGlobalRecoveryMutex();
                }
                item.Completion.TrySetResult(true);
                try
                {
                    process.Dispose();
                }
                catch { }
            }
        });
    }

    private static void UpdateGlobalRecoveryMutex()
    {
        // 必须在 lock (Gate) 内部调用
        if (TrackedItems.Count > 0)
        {
            if (inFlightGlobalMutex == null)
            {
                try
                {
                    inFlightGlobalMutex = new Mutex(true, @"Global\PCControlCenter.InFlightRecovery", out _);
                }
                catch { }
            }
        }
        else
        {
            if (inFlightGlobalMutex != null)
            {
                try
                {
                    inFlightGlobalMutex.ReleaseMutex();
                }
                catch { }
                try
                {
                    inFlightGlobalMutex.Dispose();
                }
                catch { }
                inFlightGlobalMutex = null;
            }
        }
    }

    public static bool HasInFlightRecovery
    {
        get
        {
            lock (Gate)
            {
                TrackedItems.RemoveAll(item =>
                {
                    try
                    {
                        if (item.Process != null && !item.Process.HasExited)
                            return false;
                    }
                    catch { }

                    try
                    {
                        using var live = Process.GetProcessById(item.Pid);
                        return live.HasExited;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                    catch
                    {
                        return true;
                    }
                });

                UpdateGlobalRecoveryMutex();

                if (TrackedItems.Count > 0)
                    return true;
            }

            // 跨进程探测全局恢复互斥锁：任何存活的 Broker 正在管理恢复时均能感知
            try
            {
                if (Mutex.TryOpenExisting(@"Global\PCControlCenter.InFlightRecovery", out var extMutex))
                {
                    extMutex.Dispose();
                    return true;
                }
            }
            catch { }

            return false;
        }
    }

    public static void ClearForTesting()
    {
        lock (Gate)
        {
            foreach (var item in TrackedItems)
            {
                try
                {
                    if (item.Process != null && !item.Process.HasExited)
                        item.Process.Kill(true);
                }
                catch { }
                try
                {
                    using var live = Process.GetProcessById(item.Pid);
                    if (!live.HasExited)
                        live.Kill(true);
                }
                catch { }
                try
                {
                    item.Process?.Dispose();
                }
                catch { }
            }
            TrackedItems.Clear();
            UpdateGlobalRecoveryMutex();
        }
    }
}
