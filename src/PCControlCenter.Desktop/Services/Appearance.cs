using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PCControlCenter.Desktop.Services;

public sealed record ThemePalette(string Id, string Name, string Accent, string Secondary, string Background,
    string Surface, string Inset, string Foreground, string Muted, string Line, bool Animated);

public sealed class AppearancePreferences
{
    public string Theme { get; set; } = "ice";
    public bool Motion { get; set; } = true;
    public bool SidebarExpanded { get; set; } = true;
    public string FeedbackRepository { get; set; } = FeedbackComposer.DefaultRepository;
    public bool MinimizeToTray { get; set; } = true;
    public int PollSeconds { get; set; } = 3;
}

public static class Appearance
{
    public static readonly IReadOnlyList<ThemePalette> Palettes = new[]
    {
        new ThemePalette("ice", "冰蓝", "#1478BC", "#56C4CE", "#EAF4FA", "#C4FFFFFF", "#88DDEFF9", "#16354B", "#526C7D", "#B8FFFFFF", true),
        new ThemePalette("lilac", "浅紫", "#7250B6", "#B19ADA", "#F1EDF9", "#C9FFFFFF", "#88E9DFF8", "#34254D", "#71617F", "#C0FFFFFF", true),
        new ThemePalette("mint", "薄荷", "#087D74", "#79C8B2", "#EAF5F1", "#C4FFFFFF", "#88D6EFE5", "#183E38", "#4D7169", "#C0FFFFFF", true),
        new ThemePalette("black", "高级黑", "#8EBBFF", "#A7A0E8", "#101216", "#F21D2128", "#FF262C35", "#EEF2F8", "#ACB7C6", "#FF353E4B", false),
        new ThemePalette("white", "简约白", "#285EAA", "#7B90AD", "#F4F5F7", "#FFFFFFFF", "#FFF2F4F7", "#202A38", "#606D7E", "#FFE0E5EB", false)
    };
    public static AppearancePreferences Preferences { get; private set; } = new();
    public static ThemePalette Current { get; private set; } = Palettes[0];
    public static event Action? Changed;
    public static bool Preview
    {
        get; set;
    }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCControlCenter", "appearance.json");

    public static void Load()
    {
        try
        {
            Preferences = JsonSerializer.Deserialize<AppearancePreferences>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Preferences = new(); }
        if (string.IsNullOrWhiteSpace(Preferences.FeedbackRepository))
            Preferences.FeedbackRepository = FeedbackComposer.DefaultRepository;
        Preferences.PollSeconds = Math.Clamp(Preferences.PollSeconds, 2, 30);
        Apply(Preferences.Theme, false);
    }
    public static void Save()
    {
        if (Preview)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(Preferences));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static void Apply(string id, bool save = true)
    {
        Current = Palettes[0];
        foreach (var palette in Palettes)
        {
            if (palette.Id == id)
                Current = palette;
        }
        Preferences.Theme = Current.Id;
        if (Application.Current is { } app)
        {
            foreach (var pair in new Dictionary<string, string>
            {
                ["Accent"] = Current.Accent,
                ["Secondary"] = Current.Secondary,
                ["WindowSurface"] = Current.Background,
                ["CardSurface"] = Current.Surface,
                ["InsetSurface"] = Current.Inset,
                ["TextPrimary"] = Current.Foreground,
                ["TextMuted"] = Current.Muted,
                ["SurfaceLine"] = Current.Line,
                ["InputSurface"] = Current.Id == "black" ? "#FF171B21" : "#EDFFFFFF",
                ["TrackSurface"] = Current.Id == "black" ? "#FF35404E" : "#FFC9DCE7",
                ["AccentText"] = Current.Id == "black" ? "#FF101722" : "#FFFFFFFF"
            })
                app.Resources[pair.Key] = ColorBrush(pair.Value);
        }
        if (save)
            Save();
        Changed?.Invoke();
    }
    public static void SetMotion(bool enabled)
    {
        Preferences.Motion = enabled;
        Save();
        Changed?.Invoke();
    }
    public static SolidColorBrush ColorBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
