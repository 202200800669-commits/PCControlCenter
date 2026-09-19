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

    public static void WriteReceipt(SessionReceiptRecord record)
    {
        lock (Gate)
        {
            try
            {
                var dir = GetSessionDirectory();
                var targetFile = Path.Combine(dir, $"session_{record.RequestId}.json");
                var tempFile = Path.Combine(dir, $"session_{record.RequestId}_{Guid.NewGuid():N}.tmp");
                var json = JsonSerializer.Serialize(record);
                File.WriteAllText(tempFile, json, Encoding.UTF8);
                File.Move(tempFile, targetFile, overwrite: true);
            }
            catch { }
        }
    }

    public static SessionReceiptRecord? GetReceipt(string requestId)
    {
        lock (Gate)
        {
            try
            {
                var dir = GetSessionDirectory();
                var targetFile = Path.Combine(dir, $"session_{requestId}.json");
                if (!File.Exists(targetFile))
                    return null;
                var json = File.ReadAllText(targetFile, Encoding.UTF8);
                return JsonSerializer.Deserialize<SessionReceiptRecord>(json);
            }
            catch
            {
                return null;
            }
        }
    }

    public static void Clear(string requestId)
    {
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
