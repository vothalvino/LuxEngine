using LuxEngine.Core.Cues;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Presets;

namespace LuxEngine.Core.Shows;

public enum OutputType
{
    ArtNet,
    Sacn,
    EnttecPro,
    OpenDmx,
}

/// <summary>Sends one LuxEngine universe to one physical/network output.</summary>
public sealed class OutputConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OutputType Type { get; set; } = OutputType.ArtNet;
    public bool Enabled { get; set; } = true;

    /// <summary>LuxEngine universe (1-based) to send.</summary>
    public int Universe { get; set; } = 1;

    /// <summary>Art-Net: IP or broadcast address. sACN: empty for multicast, or a unicast IP.
    /// USB: COM port name such as "COM3".</summary>
    public string Target { get; set; } = "";

    /// <summary>Universe number on the wire (Art-Net port-address 0-32767, sACN 1-63999).</summary>
    public int NetworkUniverse { get; set; }

    /// <summary>Local IP of the network adapter to send from; empty means let Windows choose.</summary>
    public string LocalAddress { get; set; } = "";
}

public sealed class MidiMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>"Note" or "ControlChange".</summary>
    public string MessageType { get; set; } = "ControlChange";
    public int Channel { get; set; }
    public int Number { get; set; }

    /// <summary>What it controls: GrandMaster, Go, Back, Pause, Blackout, ProgrammerDimmer or Preset.</summary>
    public string Action { get; set; } = "GrandMaster";

    /// <summary>Preset id for the Preset action.</summary>
    public Guid? Target { get; set; }
}

/// <summary>Everything in a .luxshow file.</summary>
public sealed class Show
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "Untitled show";

    /// <summary>Copies of every fixture model used, so a show opens on any computer.</summary>
    public Dictionary<string, FixtureDefinition> FixtureDefinitions { get; set; } = new();

    public List<FixtureInstance> Fixtures { get; set; } = new();
    public PresetFolder PresetRoot { get; set; } = new() { Name = "Presets" };
    public CueList CueList { get; set; } = new();
    public List<OutputConfig> Outputs { get; set; } = new();
    public List<MidiMapping> MidiMappings { get; set; } = new();

    public FixtureInstance? FindFixture(Guid id) => Fixtures.FirstOrDefault(f => f.Id == id);

    public FixtureDefinition? DefinitionOf(FixtureInstance fixture) =>
        FixtureDefinitions.GetValueOrDefault(fixture.DefinitionId);

    public IEnumerable<PatchedFixture> Patched()
    {
        foreach (var fixture in Fixtures)
        {
            var def = DefinitionOf(fixture);
            if (def is not null) yield return new PatchedFixture(fixture, def);
        }
    }

    /// <summary>Removes a fixture and every preset value / effect that refers to it.</summary>
    public void RemoveFixture(Guid id)
    {
        Fixtures.RemoveAll(f => f.Id == id);
        foreach (var preset in PresetRoot.AllPresets())
        {
            preset.Values.RemoveAll(v => v.FixtureId == id);
            foreach (var effect in preset.Effects) effect.FixtureIds.Remove(id);
            preset.Effects.RemoveAll(e => e.FixtureIds.Count == 0);
        }
    }

    /// <summary>Removes a preset and any cue references to it.</summary>
    public void RemovePresetOrFolder(Guid id)
    {
        var removedPresets = new HashSet<Guid>();
        var parent = PresetRoot.FindParentOf(id);
        if (parent is null) return;
        var folder = parent.Folders.FirstOrDefault(f => f.Id == id);
        if (folder is not null) removedPresets.UnionWith(folder.AllPresets().Select(p => p.Id));
        else removedPresets.Add(id);

        PresetRoot.Remove(id);
        foreach (var cue in CueList.Cues) cue.PresetIds.RemoveAll(removedPresets.Contains);
    }

    /// <summary>A starter show with Art-Net output and example folders.</summary>
    public static Show CreateNew()
    {
        var show = new Show();
        show.PresetRoot.Folders.Add(new PresetFolder { Name = "Colors" });
        show.PresetRoot.Folders.Add(new PresetFolder { Name = "Positions" });
        show.PresetRoot.Folders.Add(new PresetFolder { Name = "Effects" });
        show.Outputs.Add(new OutputConfig { Type = OutputType.ArtNet, Universe = 1, Target = "255.255.255.255", NetworkUniverse = 0 });
        return show;
    }
}
