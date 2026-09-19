using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCControlCenter.Desktop.ViewModels;

namespace PCControlCenter.Desktop.Services;

public interface IProfileStorage
{
    Task<List<ProfileItem>?> LoadProfilesAsync(CancellationToken ct = default);
    Task<bool> SaveProfilesAsync(IReadOnlyList<ProfileItem> profiles, CancellationToken ct = default);
}

public sealed class JsonProfileStorage : IProfileStorage
{
    private readonly string filePath;

    public JsonProfileStorage(string? customPath = null)
    {
        filePath = customPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCControlCenter", "profiles.json");
    }

    public async Task<List<ProfileItem>?> LoadProfilesAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            var json = await File.ReadAllTextAsync(filePath, ct);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonSerializer.Deserialize<List<ProfileItem>>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveProfilesAsync(IReadOnlyList<ProfileItem> profiles, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempFile = Path.Combine(dir ?? ".", $"profiles_{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tempFile, json, ct);

            File.Move(tempFile, filePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
