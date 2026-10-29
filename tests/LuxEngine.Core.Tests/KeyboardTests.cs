using LuxEngine.Core.Cues;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Tests;

public class KeyboardTests
{
    private static (Show Show, FixtureInstance Spot, LightingEngine Engine) Spot()
    {
        var show = new Show();
        var def = FixtureLibrary.CreateGeneric("Spot", ChannelType.Intensity, ChannelType.Gobo, ChannelType.Pan, ChannelType.Tilt, ChannelType.ColorWheel);
        var gobo = def.Channels.First(c => c.Type == ChannelType.Gobo);
        gobo.Capabilities.AddRange(new[]
        {
            new ChannelCapability { From = 0, To = 9, Label = "Open" },
            new ChannelCapability { From = 10, To = 19, Label = "Gobo 1" },
            new ChannelCapability { From = 20, To = 29, Label = "Gobo 2" },
        });
        var spot = PatchHelper.AddFixtures(show, def, def.Modes[0].Name, 1, 1, 1)[0];
        return (show, spot, new LightingEngine(show));
    }

    [Fact]
    public void Nudging_moves_from_each_value_and_stays_in_range()
    {
        var t = new TestShow();
        var engine = new LightingEngine(t.Show);
        engine.SetValue(t.Par1.Id, "Intensity", 250);
        engine.SetValue(t.Par2.Id, "Intensity", 10);
        engine.NudgeType(new[] { t.Par1.Id, t.Par2.Id }, ChannelType.Intensity, 13);
        Assert.Equal(255, engine.GetPlaybackValue(t.Par1.Id, "Intensity"));
        Assert.Equal(23, engine.GetPlaybackValue(t.Par2.Id, "Intensity"));
        engine.NudgeType(new[] { t.Par2.Id }, ChannelType.Intensity, -100);
        Assert.Equal(0, engine.GetPlaybackValue(t.Par2.Id, "Intensity"));
    }

    [Fact]
    public void Stepping_ranges_lands_in_the_middle_and_stops_at_the_ends()
    {
        var (_, spot, engine) = Spot();
        var ids = new[] { spot.Id };
        Assert.Equal("Gobo 1", engine.StepRange(ids, ChannelType.Gobo, +1));
        Assert.Equal(14, engine.GetPlaybackValue(spot.Id, "Gobo"));
        Assert.Equal("Gobo 2", engine.StepRange(ids, ChannelType.Gobo, +1));
        Assert.Equal("Gobo 2", engine.StepRange(ids, ChannelType.Gobo, +1));   // last one: stays
        Assert.Equal("Gobo 1", engine.StepRange(ids, ChannelType.Gobo, -1));
        Assert.Equal("Gobo 1", engine.RangeName(ids, ChannelType.Gobo));
        Assert.Null(engine.StepRange(ids, ChannelType.ColorWheel, +1));          // no named ranges
    }

    [Fact]
    public void Highlight_shows_full_centred_light_but_is_never_recorded()
    {
        var (_, spot, engine) = Spot();
        engine.SetValue(spot.Id, "Pan", 10);
        engine.SetValue(spot.Id, "Gobo", 25);
        engine.SetHighlight(new[] { spot.Id });

        var u = engine.ComputeFrame(0).Universe(1);
        Assert.Equal(255, u[0]);   // dimmer full
        Assert.InRange(u[1], 0, 9);  // gobo in its "Open" range
        Assert.Equal(128, u[2]);   // pan centred

        var recorded = engine.RecordPreset("x");
        Assert.DoesNotContain(recorded.Values, v => v.Channel == "Intensity");
        Assert.Contains(recorded.Values, v => v.Channel == "Pan" && v.Value == 10);

        engine.ClearHighlight();
        Assert.Equal(10, engine.ComputeFrame(0).Universe(1)[2]);
    }

    [Fact]
    public void Warm_cold_balance_keeps_brightness()
    {
        var show = new Show();
        var def = FixtureLibrary.CreateGeneric("Panel", ChannelType.WarmWhite, ChannelType.ColdWhite);
        var panel = PatchHelper.AddFixtures(show, def, def.Modes[0].Name, 1, 1, 1)[0];
        var engine = new LightingEngine(show);
        engine.SetValue(panel.Id, "WarmWhite", 200);
        engine.SetColdness(new[] { panel.Id }, 1);
        Assert.Equal(0, engine.GetPlaybackValue(panel.Id, "WarmWhite"));
        Assert.Equal(200, engine.GetPlaybackValue(panel.Id, "ColdWhite"));
        engine.SetColdness(new[] { panel.Id }, 0.5);
        Assert.Equal(0.5, engine.GetColdness(new[] { panel.Id }));
    }

    [Fact]
    public void Stage_order_is_left_to_right_then_top_to_bottom()
    {
        var a = new FixtureInstance { Name = "a", StageX = 0.5, StageY = 0.9 };
        var b = new FixtureInstance { Name = "b", StageX = 0.1 };
        var c = new FixtureInstance { Name = "c", StageX = 0.5, StageY = 0.1 };
        Assert.Equal("bca", string.Concat(new[] { a, b, c }.OrderByStage().Select(f => f.Name)));
    }

    [Fact]
    public void Keys_are_saved_with_the_show()
    {
        var t = new TestShow();
        var preset = t.AddPreset("x");
        preset.Key = "Number1";
        t.Show.ButtonPage.Buttons.Add(new CueButton { Key = "Q" });
        var loaded = ShowSerializer.Deserialize(ShowSerializer.Serialize(t.Show));
        Assert.Equal("Number1", loaded.PresetRoot.Presets.Single().Key);
        Assert.Equal("Q", loaded.ButtonPage.Buttons.Single().Key);
    }
}
