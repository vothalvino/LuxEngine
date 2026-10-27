using LuxEngine.Core.Cues;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Presets;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Tests;

/// <summary>A small show with two dimmer+RGB pars at 1 and 5 and one RGB-only par at 9.</summary>
internal sealed class TestShow
{
    public Show Show { get; } = new();
    public FixtureInstance Par1 { get; }
    public FixtureInstance Par2 { get; }
    public FixtureInstance NoDimmer { get; }

    public TestShow()
    {
        var drgb = FixtureLibrary.CreateGeneric("DRGB Par", ChannelType.Intensity, ChannelType.Red, ChannelType.Green, ChannelType.Blue);
        var rgb = FixtureLibrary.CreateGeneric("RGB Par", ChannelType.Red, ChannelType.Green, ChannelType.Blue);
        var added = PatchHelper.AddFixtures(Show, drgb, drgb.Modes[0].Name, 2, 1, 1);
        Par1 = added[0];
        Par2 = added[1];
        NoDimmer = PatchHelper.AddFixtures(Show, rgb, rgb.Modes[0].Name, 1, 1, null)[0];
    }

    public Preset AddPreset(string name, params (FixtureInstance fixture, string channel, byte value)[] values)
    {
        var preset = new Preset { Name = name };
        foreach (var (f, ch, v) in values) preset.Values.Add(new PresetValue(f.Id, ch, v));
        Show.PresetRoot.Presets.Add(preset);
        return preset;
    }

    public Cue AddCue(double fade, params Preset[] presets)
    {
        var cue = new Cue { Name = $"Cue {Show.CueList.Cues.Count + 1}", FadeIn = fade, FadeOut = fade };
        cue.PresetIds.AddRange(presets.Select(p => p.Id));
        Show.CueList.Cues.Add(cue);
        return cue;
    }
}
