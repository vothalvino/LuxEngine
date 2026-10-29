using LuxEngine.Core.Effects;
using LuxEngine.Core.Presets;

namespace LuxEngine.Core.Tests;

public class PresetNamerTests
{
    [Fact]
    public void Names_lights_color_and_dimmer()
    {
        var t = new TestShow();   // "DRGB Par 1", "DRGB Par 2", "RGB Par 1"
        var both = t.AddPreset("x", (t.Par1, "Intensity", 255), (t.Par1, "Blue", 255), (t.Par2, "Intensity", 255), (t.Par2, "Blue", 200));
        Assert.Equal("DRGB Pars · Blue · Full", PresetNamer.Suggest(t.Show, both));

        var one = t.AddPreset("x", (t.Par1, "Red", 255), (t.Par1, "Green", 128), (t.Par1, "Intensity", 128));
        Assert.Equal("DRGB Par 1 · Orange · 50%", PresetNamer.Suggest(t.Show, one));
    }

    [Fact]
    public void Names_all_lights_and_effects()
    {
        var t = new TestShow();
        var p = t.AddPreset("x", (t.Par1, "Intensity", 0), (t.Par2, "Intensity", 0), (t.NoDimmer, "Red", 0));
        p.Effects.Add(new Effect { Type = EffectType.Rainbow, FixtureIds = { t.Par1.Id } });
        Assert.Equal("All lights · Off · Rainbow", PresetNamer.Suggest(t.Show, p));
    }

    [Theory]
    [InlineData(1, 0, 0, "Red")]
    [InlineData(0, 0, 1, "Blue")]
    [InlineData(1, 0, 1, "Magenta")]
    [InlineData(1, 1, 1, "White")]
    [InlineData(1, 0.6, 0.8, "Pink")]
    [InlineData(0, 0, 0, null)]
    public void Hue_names(double r, double g, double b, string? expected) =>
        Assert.Equal(expected, PresetNamer.Hue(r, g, b));
}
