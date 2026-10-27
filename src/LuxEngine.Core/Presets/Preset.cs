using LuxEngine.Core.Effects;

namespace LuxEngine.Core.Presets;

/// <summary>One stored channel value of one fixture.</summary>
public sealed record PresetValue(Guid FixtureId, string Channel, byte Value);

/// <summary>A saved look: channel values for some fixtures, optionally with a running effect.</summary>
public sealed class Preset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New preset";

    /// <summary>Accent color shown in the preset panel, as "#RRGGBB".</summary>
    public string? Color { get; set; }

    public List<PresetValue> Values { get; set; } = new();
    public List<Effect> Effects { get; set; } = new();

    public IEnumerable<Guid> FixtureIds => Values.Select(v => v.FixtureId).Concat(Effects.SelectMany(e => e.FixtureIds)).Distinct();
}

/// <summary>A folder in the preset tree. Folders can contain presets and other folders.</summary>
public sealed class PresetFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New folder";
    public List<PresetFolder> Folders { get; set; } = new();
    public List<Preset> Presets { get; set; } = new();

    public IEnumerable<Preset> AllPresets() => Presets.Concat(Folders.SelectMany(f => f.AllPresets()));

    public IEnumerable<PresetFolder> AllFolders() => Folders.Concat(Folders.SelectMany(f => f.AllFolders()));

    public Preset? FindPreset(Guid id) => AllPresets().FirstOrDefault(p => p.Id == id);

    /// <summary>Finds the folder that directly contains the given preset or folder id.</summary>
    public PresetFolder? FindParentOf(Guid id)
    {
        if (Presets.Any(p => p.Id == id) || Folders.Any(f => f.Id == id)) return this;
        return Folders.Select(f => f.FindParentOf(id)).FirstOrDefault(f => f is not null);
    }

    /// <summary>Removes a preset or folder anywhere below this folder.</summary>
    public bool Remove(Guid id)
    {
        var parent = FindParentOf(id);
        if (parent is null) return false;
        return parent.Presets.RemoveAll(p => p.Id == id) + parent.Folders.RemoveAll(f => f.Id == id) > 0;
    }

    public bool Contains(Guid folderId) => Id == folderId || Folders.Any(f => f.Contains(folderId));
}
