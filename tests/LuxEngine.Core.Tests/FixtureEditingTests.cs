using LuxEngine.Core.Fixtures;

namespace LuxEngine.Core.Tests;

public class FixtureEditingTests
{
    private static EditedChannel Ch(string? from, string name, ChannelType type) =>
        new(from, new ChannelDefinition { Name = name, Type = type });

    [Fact]
    public void Editing_a_type_updates_fixtures_and_keeps_presets_working()
    {
        var t = new TestShow();
        var original = t.Show.DefinitionOf(t.Par1)!;
        var preset = t.AddPreset("Look", (t.Par1, "Intensity", 200), (t.Par1, "Red", 10), (t.Par1, "Green", 20));

        // Rename Red, drop Green, add a strobe channel and rename the mode.
        var (def, renames) = FixtureTypeEditing.EditMode(original, t.Par1.ModeName, "5ch", new[]
        {
            Ch("Intensity", "Intensity", ChannelType.Intensity),
            Ch("Red", "Red LED", ChannelType.Red),
            Ch("Blue", "Blue", ChannelType.Blue),
            Ch(null, "Strobe", ChannelType.Strobe),
        }, "Generic", "DRGB Par", "Color Changer");
        t.Show.UpdateDefinition(def, t.Par1.ModeName, "5ch", renames);

        Assert.Equal("5ch", t.Par1.ModeName);
        Assert.Equal("5ch", t.Par2.ModeName);
        Assert.Equal(new[] { "Intensity", "Red LED", "Blue", "Strobe" }, def.Modes.Single().Channels);
        Assert.Equal(new[] { ("Intensity", 200), ("Red LED", 10) },
            preset.Values.Select(v => (v.Channel, (int)v.Value)));
        Assert.Equal("RGB Par", t.Show.DefinitionOf(t.NoDimmer)!.Model);   // other types untouched
    }

    [Fact]
    public void Other_modes_keep_their_channels()
    {
        var original = FixtureLibrary.CreateGeneric("Par", ChannelType.Intensity, ChannelType.Red);
        original.Modes.Add(new FixtureMode { Name = "Dimmer only", Channels = { "Intensity" } });

        var (def, renames) = FixtureTypeEditing.EditMode(original, original.Modes[0].Name, original.Modes[0].Name,
            new[] { Ch("Red", "Red", ChannelType.Red) }, "", "Par", "Other");

        Assert.Contains(def.Channels, c => c.Name == "Intensity");
        Assert.Equal(new[] { "Intensity" }, def.Modes[1].Channels);
        Assert.Null(renames["Intensity"]);
    }
}
