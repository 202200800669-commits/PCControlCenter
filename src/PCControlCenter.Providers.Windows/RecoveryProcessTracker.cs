using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PCControlCenter.Providers.Windows;

public static class RecoveryProcessTracker
{
    private static readonly object Gate = new();
    private static readonly List<Process> TrackedProcesses = new();

    public static void Track(Process process, params string[] mutexNames)
    {
        lock (Gate)
        {
            TrackedProcesses.Add(process);
        }

        // 监视后台恢复进程自然完成回滚与互斥锁释放。
        // 设置超长安全硬上限以防极度罕见的内核级死锁永远泄漏。
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
                catch { }
            }
            catch { }
            finally
            {
                lock (Gate)
                {
                    TrackedProcesses.Remove(process);
                }
                try
                {
                    process.Dispose();
                }
                catch { }
            }
        });
    }

    public static bool HasInFlightRecovery
    {
        get
        {
            lock (Gate)
            {
                TrackedProcesses.RemoveAll(p => p.HasExited);
                return TrackedProcesses.Count > 0;
            }
        }
    }

    public static void ClearForTesting()
    {
        lock (Gate)
        {
            foreach (var p in TrackedProcesses)
            {
                try
                {
                    if (!p.HasExited)
                        p.Kill(true);
                }
                catch { }
                try
                {
                    p.Dispose();
                }
                catch { }
            }
            TrackedProcesses.Clear();
        }
    }
}
