using LuxEngine.Core.Effects;
using LuxEngine.Core.Engine;

namespace LuxEngine.Core.Tests;

public class LightingEngineTests
{
    [Fact]
    public void Cue_with_several_presets_combines_them()
    {
        var t = new TestShow();
        var dim = t.AddPreset("Dim", (t.Par1, "Intensity", 100), (t.Par2, "Intensity", 100));
        var bright = t.AddPreset("Bright par 2", (t.Par2, "Intensity", 200));
        var blue = t.AddPreset("Blue", (t.Par1, "Blue", 255), (t.Par1, "Red", 50));
        var red = t.AddPreset("Red", (t.Par1, "Red", 255));
        t.AddCue(0, bright, dim, blue, red);

        var engine = new LightingEngine(t.Show);
        engine.Go();
        var u = engine.ComputeFrame(0).Universe(1);

        Assert.Equal(100, u[0]);  // Par1 intensity
        Assert.Equal(255, u[1]);  // Par1 red: last preset wins
        Assert.Equal(255, u[3]);  // Par1 blue
        Assert.Equal(200, u[4]);  // Par2 intensity: highest wins even though "Dim" came later
    }

    [Fact]
    public void Fade_moves_halfway_at_half_time_and_completes()
    {
        var t = new TestShow();
        var on = t.AddPreset("On", (t.Par1, "Intensity", 200));
        t.AddCue(2, on);
        double now = 10;
        var engine = new LightingEngine(t.Show, () => now);

        engine.ComputeFrame();
        engine.GoTo(0);
        now = 11;
        Assert.InRange(engine.ComputeFrame().Universe(1)[0], 95, 105);
        now = 12.5;
        Assert.Equal(200, engine.ComputeFrame().Universe(1)[0]);
    }

    [Fact]
    public void Next_cue_fades_out_intensities_it_does_not_use_but_keeps_colors()
    {
        var t = new TestShow();
        var a = t.AddPreset("A", (t.Par1, "Intensity", 255), (t.Par1, "Green", 255));
        var b = t.AddPreset("B", (t.Par2, "Intensity", 255));
        t.AddCue(0, a);
        t.AddCue(0, b);
        var engine = new LightingEngine(t.Show);

        engine.Go();
        engine.ComputeFrame();
        engine.Go();
        var u = engine.ComputeFrame().Universe(1);

        Assert.Equal(0, u[0]);    // Par1 intensity released
        Assert.Equal(255, u[2]);  // Par1 green tracks
        Assert.Equal(255, u[4]);  // Par2 on
    }

    [Fact]
    public void Programmer_overrides_cue_and_grand_master_scales_dimmers()
    {
        var t = new TestShow();
        t.AddCue(0, t.AddPreset("A", (t.Par1, "Intensity", 255)));
        var engine = new LightingEngine(t.Show);
        engine.Go();
        engine.SetValue(t.Par1.Id, "Intensity", 100);
        engine.SetValue(t.NoDimmer.Id, "Red", 200);
        engine.GrandMaster = 0.5;

        var u = engine.ComputeFrame().Universe(1);
        Assert.Equal(50, u[0]);
        Assert.Equal(100, u[8]); // RGB-only fixture is scaled through its colors

        engine.Blackout = true;
        u = engine.ComputeFrame().Universe(1);
        Assert.Equal(0, u[0]);
        Assert.Equal(0, u[8]);
    }

    [Fact]
    public void Chase_lights_one_fixture_at_a_time()
    {
        var t = new TestShow();
        var engine = new LightingEngine(t.Show);
        engine.SetValue(new[] { t.Par1.Id, t.Par2.Id }, Fixtures.ChannelType.Intensity, 255);
        engine.AddEffect(new Effect { Type = EffectType.Chase, Speed = 1, Size = 1, FixtureIds = { t.Par1.Id, t.Par2.Id } });

        var first = engine.ComputeFrame(0.1).Universe(1);
        Assert.Equal(255, first[0]);
        Assert.Equal(0, first[4]);

        var second = engine.ComputeFrame(0.6).Universe(1);
        Assert.Equal(0, second[0]);
        Assert.Equal(255, second[4]);
    }

    [Fact]
    public void Back_returns_to_previous_cue()
    {
        var t = new TestShow();
        t.AddCue(0, t.AddPreset("A", (t.Par1, "Intensity", 10)));
        t.AddCue(0, t.AddPreset("B", (t.Par1, "Intensity", 20)));
        var engine = new LightingEngine(t.Show);
        engine.Go();
        engine.Go();
        Assert.Equal(20, engine.ComputeFrame().Universe(1)[0]);
        engine.Back();
        Assert.Equal(0, engine.CuePlayer.CurrentIndex);
        Assert.Equal(10, engine.ComputeFrame().Universe(1)[0]);
    }

    [Fact]
    public void Recorded_preset_contains_programmer_values_for_selected_fixtures()
    {
        var t = new TestShow();
        var engine = new LightingEngine(t.Show);
        engine.SetValue(t.Par1.Id, "Red", 255);
        engine.SetValue(t.Par2.Id, "Red", 128);

        var preset = engine.RecordPreset("Red", new[] { t.Par2.Id });

        var value = Assert.Single(preset.Values);
        Assert.Equal(t.Par2.Id, value.FixtureId);
        Assert.Equal(128, value.Value);
    }
}
