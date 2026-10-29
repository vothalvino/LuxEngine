using LuxEngine.Core.Shows;
using Windows.System;

namespace LuxEngine.App.Services;

/// <summary>Names of keyboard keys and the rules for keys people can give to presets and cue buttons.</summary>
public static class KeyNames
{
    private const VirtualKey OpenBracket = (VirtualKey)219, CloseBracket = (VirtualKey)221, QuestionMark = (VirtualKey)191;

    /// <summary>Keys LuxEngine itself uses without modifiers, so they cannot be given to a preset or button.</summary>
    private static readonly HashSet<VirtualKey> Reserved = new()
    {
        VirtualKey.Space, VirtualKey.B, VirtualKey.P, VirtualKey.H, VirtualKey.Escape, VirtualKey.Enter,
        VirtualKey.Back, VirtualKey.Delete, VirtualKey.Tab, VirtualKey.F1, VirtualKey.F2, VirtualKey.F3, VirtualKey.F4, VirtualKey.F11,
    };

    /// <summary>Letters, digits (top row and number pad) and F5–F10/F12, except the reserved keys.</summary>
    public static bool IsAssignable(VirtualKey key) =>
        !Reserved.Contains(key) &&
        (key is >= VirtualKey.A and <= VirtualKey.Z
         || key is >= VirtualKey.Number0 and <= VirtualKey.Number9
         || key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9
         || key is >= VirtualKey.F5 and <= VirtualKey.F12);

    public static string Reason(VirtualKey key) => Reserved.Contains(key)
        ? $"{Display(key)} is already used by LuxEngine (see Help > Keyboard shortcuts)."
        : "Use a letter, a digit or F5–F12.";

    public static string Display(VirtualKey key) => key switch
    {
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)key - (int)VirtualKey.Number0).ToString(),
        >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => $"Num {(int)key - (int)VirtualKey.NumberPad0}",
        OpenBracket => "[",
        CloseBracket => "]",
        QuestionMark => "?",
        VirtualKey.Back => "Backspace",
        _ => key.ToString(),
    };

    public static string Display(string? key) =>
        key is not null && Enum.TryParse<VirtualKey>(key, out var k) ? Display(k) : "";

    /// <summary>What already uses this key in the show (another preset or cue button), or null if it is free.</summary>
    public static string? UsedBy(Show show, string key, object? self)
    {
        var preset = show.PresetRoot.AllPresets().FirstOrDefault(p => p.Key == key && !ReferenceEquals(p, self));
        if (preset is not null) return $"the preset \"{preset.Name}\"";
        var button = show.ButtonPage.Buttons.FirstOrDefault(b => b.Key == key && !ReferenceEquals(b, self));
        return button is null ? null : $"the cue button \"{button.Name}\"";
    }
}
