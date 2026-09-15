using System.Text.Json;
using System.Security.Cryptography;
namespace PCControlCenter.Core;

public sealed record Preferences(int SchemaVersion = 1, bool MinimizeToTray = true, int PollIntervalSeconds = 3);
public sealed record PreferenceState(Preferences Value, string Revision);
public sealed record PreferenceUpdate(PreferenceState State, string BackupPath);
public static class PreferenceStore
{
    public static Preferences Load(string path)
    {
        return Read(path).Value;
    }
    public static PreferenceState Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 16384)
            throw new InvalidDataException("Configuration too large");
        using var data = new MemoryStream();
        input.CopyTo(data);
        var bytes = data.ToArray();
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid configuration");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in document.RootElement.EnumerateObject())
        {
            if (field.Name is not ("SchemaVersion" or "MinimizeToTray" or "PollIntervalSeconds") || !names.Add(field.Name))
                throw new InvalidDataException("Unknown or duplicate setting");
        }
        if (names.Count != 3)
            throw new InvalidDataException("Incomplete configuration");
        var p = JsonSerializer.Deserialize<Preferences>(bytes) ?? throw new InvalidDataException("Empty configuration");
        Validate(p);
        return new(p, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public static PreferenceUpdate Update(string path, string expectedRevision, Preferences preferences)
    {
        Validate(preferences);
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full)!;
        // A stable lock file serializes cooperating writers. Do not unlink it while
        // another process could hold an open handle to the same lock inode.
        using var gate = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Read(full).Revision != expectedRevision)
            throw new InvalidOperationException("CONFIGURATION_CONFLICT");
        var temporary = Path.Combine(parent, ".preferences-" + Guid.NewGuid() + ".tmp");
        var backup = full + ".backup-" + Guid.NewGuid().ToString("N") + ".json";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, preferences, Diagnostics.Json);
                output.Flush(true);
            }
            File.Replace(temporary, full, backup);
            return new(Read(full), backup);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static PreferenceUpdate Restore(string path, string expectedRevision, string backupPath)
    {
        return Update(path, expectedRevision, Load(backupPath));
    }
    private static void Validate(Preferences p)
    {
        if (p.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported configuration version");
        if (p.PollIntervalSeconds is < 1 or > 30)
            throw new InvalidDataException("Invalid poll interval");
    }
    public static void Create(string destination, Preferences preferences)
    {
        Validate(preferences);
        var full = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(full)!;
        // Callers select the data folder; never silently overwrite existing settings.
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".preferences-" + Guid.NewGuid() + ".tmp");
        try
        {
            using (var f = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(f, preferences, Diagnostics.Json);
                f.Flush(true);
            }
            File.Move(temporary, full, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static Preferences ImportLegacy(string source, string destination)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(source));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid legacy configuration");
        bool tray = true;
        int interval = 3;
        if (root.TryGetProperty("Tray", out var t))
        {
            if (t.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Invalid tray preference");
            tray = t.GetBoolean();
        }
        if (root.TryGetProperty("Interval", out var i) && !i.TryGetInt32(out interval))
            throw new InvalidDataException("Invalid interval");
        var p = new Preferences(1, tray, interval);
        // Explicit allowlist excludes Profiles, fan targets, power limits and visual assets.
        Create(destination, p);
        return p;
    }
}
