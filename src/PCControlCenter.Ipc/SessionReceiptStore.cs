using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PCControlCenter.Ipc;

public sealed record SessionReceiptRecord(
    string RequestId,
    string Operation,
    string State,
    string? Recovery = null,
    string? Code = null,
    string? Message = null,
    long TimestampUtc = 0);

public static class RecoveryOutcome
{
    public static bool IsConfirmed(string? recovery, string? code) => recovery switch
    {
        "AUTO_COMMANDS_SENT_OVERRIDE_OFF" or "RESTORED_PREVIOUS" => true,
        "NOT_NEEDED" => code is "COMPLETED" or "CANCELLED_BEFORE_WRITE",
        _ => false
    };

    public static string TerminalState(string? recovery, string? code) =>
        IsConfirmed(recovery, code) ? "Completed" : "TerminatedUnconfirmed";
}

public static class SessionReceiptStore
{
    private static readonly object Gate = new();

    public static string GetSessionDirectory()
    {
        string baseDir;
        try
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCControlCenter", "sessions");
            if (!Directory.Exists(baseDir))
                Directory.CreateDirectory(baseDir);
        }
        catch
        {
            baseDir = Path.Combine(Path.GetTempPath(), "PCControlCenter", "sessions");
            if (!Directory.Exists(baseDir))
                Directory.CreateDirectory(baseDir);
        }
        return baseDir;
    }

    public static void WriteReceipt(SessionReceiptRecord record, string? directory = null)
    {
        if (!Guid.TryParseExact(record.RequestId, "N", out _))
            throw new ArgumentException("Invalid request id");
        lock (Gate)
        {
            try
            {
                var dir = directory ?? GetSessionDirectory();
                Directory.CreateDirectory(dir);
                var targetFile = Path.Combine(dir, $"session_{record.RequestId}.json");
                var tempFile = Path.Combine(dir, $"session_{record.RequestId}_{Guid.NewGuid():N}.tmp");
                var json = JsonSerializer.Serialize(record);
                File.WriteAllText(tempFile, json, Encoding.UTF8);
                File.Move(tempFile, targetFile, overwrite: true);
            }
            catch { }
        }
    }

    public static SessionReceiptRecord? GetReceipt(string requestId, string? directory = null)
    {
        if (!Guid.TryParseExact(requestId, "N", out _))
            return null;
        lock (Gate)
        {
            try
            {
                var dir = directory ?? GetSessionDirectory();
                var targetFile = Path.Combine(dir, $"session_{requestId}.json");
                if (!File.Exists(targetFile))
                    return null;
                var json = File.ReadAllText(targetFile, Encoding.UTF8);
                var record = JsonSerializer.Deserialize<SessionReceiptRecord>(json);
                return record?.RequestId == requestId ? record : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public static void Clear(string requestId)
    {
        if (!Guid.TryParseExact(requestId, "N", out _))
            return;
        lock (Gate)
        {
            try
            {
                var dir = GetSessionDirectory();
                var targetFile = Path.Combine(dir, $"session_{requestId}.json");
                if (File.Exists(targetFile))
                    File.Delete(targetFile);
            }
            catch { }
        }
    }
}
