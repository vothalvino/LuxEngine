using LuxEngine.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LuxEngine.App.Helpers;

/// <summary>
/// Theme-aware resources for UI built in code. Application.Current.Resources only knows the Windows
/// theme, but LuxEngine can be switched to Light or Dark on its own (View menu), so look the
/// resource up in the theme the window really shows.
/// </summary>
public static class Ui
{
    /// <summary>Raised after View > Light/Dark changes the theme, so code-built UI can be rebuilt.</summary>
    public static event Action? ThemeChanged;

    public static void RaiseThemeChanged() => ThemeChanged?.Invoke();

    private static string ThemeKey =>
        (AppServices.MainWindow?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Light ? "Light" : "Dark";

    public static Brush Brush(string key) => (Brush)Resource(key);

    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static object Resource(string key)
    {
        var root = Application.Current.Resources;
        // Dark is called "Default" in the WinUI control resources.
        foreach (var theme in ThemeKey == "Light" ? new[] { "Light" } : new[] { "Dark", "Default" })
            if (Find(root, key, theme) is { } value) return value;
        return root[key];
    }

    private static object? Find(ResourceDictionary dictionary, string key, string theme)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var t) && t is ResourceDictionary themed && themed.TryGetValue(key, out var v))
            return v;
        foreach (var merged in dictionary.MergedDictionaries)
            if (Find(merged, key, theme) is { } found) return found;
        return null;
    }
}
