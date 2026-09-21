using System.Diagnostics;

namespace PCControlCenter.Providers.Windows;

public static class RecoveryProcessTracker
{
    private const string MarkerName = @"Global\PCControlCenter.InFlightRecovery";
    private static readonly object Gate = new();
    private sealed record Entry(Process Process, TaskCompletionSource Completion);
    private static readonly List<Entry> Entries = new();
    private static Mutex? marker;

    // Keep an independent OS process handle; the caller owns its original wrapper and output streams.
    // The named object is a presence marker, never an owned/thread-affine mutex.
    public static Task WaitForCompletionAsync(Process process, string requestId = "")
    {
        if (process.HasExited)
            return Task.CompletedTask;
        Process owned;
        try
        {
            owned = Process.GetProcessById(process.Id);
        }
        catch (ArgumentException) when (process.HasExited) { return Task.CompletedTask; }
        try
        {
            _ = owned.SafeHandle;
        }
        catch when (process.HasExited) { owned.Dispose(); return Task.CompletedTask; }
        var entry = new Entry(owned, new(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (Gate)
        {
            try
            {
                marker ??= new Mutex(false, MarkerName);
                Entries.Add(entry);
            }
            catch { owned.Dispose(); throw; }
        }
        _ = ObserveAsync(entry);
        return entry.Completion.Task;
    }

    private static async Task ObserveAsync(Entry entry)
    {
        Exception? failure = null;
        try
        {
            await entry.Process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            lock (Gate)
            {
                Entries.Remove(entry);
                if (Entries.Count == 0)
                {
                    marker?.Dispose();
                    marker = null;
                }
            }
            entry.Process.Dispose();
        }
        if (failure is null)
            entry.Completion.TrySetResult();
        else
            entry.Completion.TrySetException(failure);
    }

    public static void Track(Process process, params string[] mutexNames) => Track(process, "", null);
    public static void Track(Process process, string requestId, string? mutexName = null) =>
        _ = WaitForCompletionAsync(process, requestId);

    public static bool HasInFlightRecovery
    {
        get
        {
            lock (Gate)
            {
                if (Entries.Count > 0)
                    return true;
            }
            try
            {
                if (Mutex.TryOpenExisting(MarkerName, out var existing))
                {
                    existing.Dispose();
                    return true;
                }
            }
            catch (UnauthorizedAccessException) { return true; }
            return false;
        }
    }

    public static void ClearForTesting()
    {
        Entry[] entries;
        lock (Gate)
            entries = Entries.ToArray();
        foreach (var entry in entries)
        {
            try
            {
                if (!entry.Process.HasExited)
                    entry.Process.Kill(true);
            }
            catch (InvalidOperationException) { }
        }
        Task.WhenAll(entries.Select(x => x.Completion.Task)).GetAwaiter().GetResult();
    }
}
