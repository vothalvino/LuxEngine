using System.Text.Json;
using LuxEngine.Core.Shows;

namespace LuxEngine.App.Services;

public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\LuxEngine</summary>
    public static string DataFolder
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuxEngine");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Fixtures the user created: Documents\LuxEngine\Fixtures, easy to back up and share.</summary>
    public static string UserFixtures =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LuxEngine", "Fixtures");

    public static string BundledFixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
}

/// <summary>Per-user preferences, stored next to the autosave.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Default";
    public List<string> RecentShows { get; set; } = new();
    public double RefreshRate { get; set; } = 40;
    public bool FirstRunDone { get; set; }
    public string AudioSource { get; set; } = "SystemSound";

    /// <summary>Height of the Control / Cue area, set by dragging the handle above it.</summary>
    public double BottomHeight { get; set; } = 380;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), ShowSerializer.Options) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, ShowSerializer.Options));
        }
        catch (IOException)
        {
        }
    }

    public void AddRecent(string path)
    {
        RecentShows.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentShows.Insert(0, path);
        if (RecentShows.Count > 8) RecentShows.RemoveRange(8, RecentShows.Count - 8);
        Save();
    }
}
