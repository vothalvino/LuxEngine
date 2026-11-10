using System.Text.Json;
using Lumeshow.Core.Migration;
using Lumeshow.Core.Shows;

namespace Lumeshow.App.Services;

public static class AppPaths
{
    private const string Name = "Lumeshow";

    // Each folder is worked out once: the first use also brings over what a LuxEngine version left behind.
    private static readonly Lazy<string> Data = new(() =>
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(local, Name);
        if (LegacyFolders.Move(Path.Combine(local, LegacyFolders.OldName), path)) RenameLegacyAutosave(path);
        Directory.CreateDirectory(path);
        return path;
    });

    private static readonly Lazy<string> Fixtures = new(() =>
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(documents, Name, "Fixtures");
        // Only the fixtures move. Anything else in Documents\LuxEngine (saved shows, for example) stays where the user put it.
        var old = Path.Combine(documents, LegacyFolders.OldName);
        if (LegacyFolders.Move(Path.Combine(old, "Fixtures"), path)) LegacyFolders.RemoveIfEmpty(old);
        return path;
    });

    /// <summary>%LOCALAPPDATA%\Lumeshow</summary>
    public static string DataFolder => Data.Value;

    /// <summary>Fixtures the user created: Documents\Lumeshow\Fixtures, easy to back up and share.</summary>
    public static string UserFixtures => Fixtures.Value;

    public static string BundledFixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    /// <summary>A recovery copy written by LuxEngine keeps working: it gets the name Lumeshow looks for.</summary>
    private static void RenameLegacyAutosave(string folder)
    {
        try
        {
            var old = Path.Combine(folder, "autosave" + ShowSerializer.LegacyFileExtension);
            var now = Path.Combine(folder, "autosave" + ShowSerializer.FileExtension);
            if (File.Exists(old) && !File.Exists(now)) File.Move(old, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without it there is just no recovery offer.
        }
    }
}

/// <summary>Per-user preferences, stored next to the autosave.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Default";
    public List<string> RecentShows { get; set; } = new();
    public double RefreshRate { get; set; } = 40;
    public bool FirstRunDone { get; set; }
    public string AudioSource { get; set; } = "SystemSound";

    /// <summary>Ask GitHub once at start-up whether a newer version exists.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Height of the Control / Cue area, set by dragging the handle above it.</summary>
    public double BottomHeight { get; set; } = 380;

    /// <summary>The phone remote (Output → Phone remote). Off until the user switches it on.</summary>
    public bool RemoteEnabled { get; set; }
    public int RemotePort { get; set; } = 8080;

    /// <summary>The PIN a phone has to give. Empty means no PIN.</summary>
    public string RemotePin { get; set; } = "";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), ShowSerializer.Options) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // A missing, unreadable or broken settings file just means default settings.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, ShowSerializer.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them must not stop the show.
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
