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
    public void Cue_effects_leave_hand_controlled_fixtures_alone()
    {
        var t = new TestShow();
        var chase = t.AddPreset("Chase", (t.Par1, "Intensity", 255), (t.Par2, "Intensity", 255));
        chase.Effects.Add(new Effect { Type = EffectType.Chase, Speed = 1, Size = 1, FixtureIds = { t.Par1.Id, t.Par2.Id } });
        t.AddCue(0, chase);
        var engine = new LightingEngine(t.Show);
        engine.Go();

        engine.SetValue(t.Par2.Id, "Intensity", 200);

        var u = engine.ComputeFrame(0.1).Universe(1);
        Assert.Equal(255, u[0]);  // Par1 is lit by the chase at this moment
        Assert.Equal(200, u[4]);  // Par2 stays at the hand-set level instead of being chased off
    }

    [Fact]
    public void Playback_value_ignores_effects_and_prefers_programmer()
    {
        var t = new TestShow();
        var chase = t.AddPreset("Chase", (t.Par1, "Intensity", 180), (t.Par2, "Intensity", 180));
        chase.Effects.Add(new Effect { Type = EffectType.Chase, Speed = 1, Size = 1, FixtureIds = { t.Par1.Id, t.Par2.Id } });
        t.AddCue(0, chase);
        var engine = new LightingEngine(t.Show);
        engine.Go();
        engine.ComputeFrame(0.1);

        Assert.Equal(0, engine.GetOutput(t.Par2.Id, "Intensity"));          // chased off right now
        Assert.Equal(180, engine.GetPlaybackValue(t.Par2.Id, "Intensity")); // but the cue sets 180
        engine.SetValue(t.Par2.Id, "Intensity", 50);
        Assert.Equal(50, engine.GetPlaybackValue(t.Par2.Id, "Intensity"));
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

public class PresetEditTests
{
    [Fact]
    public void Editing_a_preset_shows_only_its_values_saves_changes_and_restores_the_live_look()
    {
        var t = new TestShow();
        var preset = t.AddPreset("Look", (t.Par1, "Intensity", 200), (t.Par1, "Red", 255));
        var engine = new LightingEngine(t.Show);
        engine.SetValue(t.Par2.Id, "Intensity", 99);           // something set by hand before editing

        var before = engine.BeginPresetEdit(preset);
        Assert.Equal(new[] { t.Par1.Id }, engine.ProgrammerFixtures());
        Assert.Equal(255, engine.GetPlaybackValue(t.Par1.Id, "Red"));

        engine.SetValue(t.Par1.Id, "Red", 128);                // a small change
        engine.SetValue(t.NoDimmer.Id, "Blue", 50);            // add another light
        engine.EndPresetEdit(preset, before, save: true);

        Assert.Equal(3, preset.Values.Count);
        Assert.Contains(new Presets.PresetValue(t.Par1.Id, "Red", 128), preset.Values);
        Assert.Equal(new[] { t.Par2.Id }, engine.ProgrammerFixtures());   // live look is back
    }

    [Fact]
    public void Cancelling_leaves_the_preset_unchanged()
    {
        var t = new TestShow();
        var preset = t.AddPreset("Look", (t.Par1, "Intensity", 200));
        var engine = new LightingEngine(t.Show);
        var before = engine.BeginPresetEdit(preset);
        engine.ReleaseFixture(t.Par1.Id);
        engine.EndPresetEdit(preset, before, save: false);
        Assert.Single(preset.Values);
        Assert.Empty(engine.ProgrammerFixtures());
    }
}

public class WhiteOnlyLookTests
{
    [Fact]
    public void Warm_cold_light_without_rgb_shows_its_real_level_and_tint()
    {
        var show = new Shows.Show();
        var panel = Fixtures.FixtureLibrary.CreateGeneric("Panel", Fixtures.ChannelType.WarmWhite, Fixtures.ChannelType.ColdWhite);
        var light = Shows.PatchHelper.AddFixtures(show, panel, panel.Modes[0].Name, 1, 1, 1)[0];
        var engine = new LightingEngine(show);

        engine.ComputeFrame(0);
        Assert.Equal(0, engine.GetLook(light.Id).Intensity);   // off is off, not white

        engine.SetValue(light.Id, panel.Channels[1].Name, 255);  // cold white full
        engine.ComputeFrame(0);
        var look = engine.GetLook(light.Id);
        Assert.Equal(1, look.Intensity, 3);
        Assert.True(look.Blue > look.Red);
    }
}

public class EffectOrderTests
{
    [Fact]
    public void Chase_runs_left_to_right_on_stage_not_in_selection_order()
    {
        var t = new TestShow();
        t.Par1.StageX = 0.9;   // Par 1 stands on the right,
        t.Par2.StageX = 0.1;   // Par 2 on the left
        var engine = new LightingEngine(t.Show);
        engine.SetValue(t.Par1.Id, "Intensity", 255);
        engine.SetValue(t.Par2.Id, "Intensity", 255);
        engine.AddEffect(new Effect { Type = EffectType.Chase, Speed = 1, Size = 1, FixtureIds = { t.Par1.Id, t.Par2.Id } });

        var first = engine.ComputeFrame(0.1).Universe(1);
        Assert.Equal(0, first[0]);     // Par 1 (right) waits
        Assert.Equal(255, first[4]);   // Par 2 (left) goes first
        var second = engine.ComputeFrame(0.6).Universe(1);
        Assert.Equal(255, second[0]);
        Assert.Equal(0, second[4]);
    }
}
