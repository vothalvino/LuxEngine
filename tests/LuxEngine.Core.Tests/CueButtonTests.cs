using LuxEngine.Core.Cues;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Presets;

namespace LuxEngine.Core.Tests;

public class CueButtonTests
{
    private static CueButton AddButton(TestShow t, int row, int column, params Preset[] presets)
    {
        var button = new CueButton { Row = row, Column = column, FadeIn = 0, FadeOut = 0 };
        button.PresetIds.AddRange(presets.Select(p => p.Id));
        t.Show.ButtonPage.Buttons.Add(button);
        return button;
    }

    [Fact]
    public void Touching_buttons_form_one_group_but_diagonals_do_not()
    {
        var t = new TestShow();
        var a = AddButton(t, 0, 0);
        var b = AddButton(t, 0, 1);
        var c = AddButton(t, 1, 1);
        var diagonal = AddButton(t, 2, 2);
        var alone = AddButton(t, 3, 5);

        Assert.Equal(new[] { a, b, c }.ToHashSet(), t.Show.ButtonPage.GroupOf(a).ToHashSet());
        Assert.Single(t.Show.ButtonPage.GroupOf(diagonal));
        Assert.Single(t.Show.ButtonPage.GroupOf(alone));
    }

    [Fact]
    public void Turning_on_a_button_turns_off_the_ones_it_touches()
    {
        var t = new TestShow();
        var a = AddButton(t, 0, 0);
        var b = AddButton(t, 0, 1);
        var separate = AddButton(t, 0, 3);
        var engine = new LightingEngine(t.Show);

        engine.SetButton(a.Id, true);
        engine.SetButton(separate.Id, true);
        engine.SetButton(b.Id, true);

        Assert.False(engine.IsButtonOn(a.Id));
        Assert.True(engine.IsButtonOn(b.Id));
        Assert.True(engine.IsButtonOn(separate.Id));
        engine.ToggleButton(b.Id);
        Assert.False(engine.IsButtonOn(b.Id));
    }

    [Fact]
    public void Buttons_play_on_top_of_the_cue_list_and_fade()
    {
        var t = new TestShow();
        var cueLook = t.AddPreset("Cue", (t.Par1, "Intensity", 100), (t.Par1, "Red", 255));
        var buttonLook = t.AddPreset("Button", (t.Par1, "Intensity", 50), (t.Par1, "Blue", 200), (t.Par1, "Red", 0), (t.Par2, "Intensity", 200));
        t.AddCue(0, cueLook);
        var button = AddButton(t, 0, 0, buttonLook);
        button.Mode = CueButtonMode.Flash;   // flash buttons play on top; toggle buttons take over (tested below)
        button.FadeIn = 2;
        double now = 0;
        var engine = new LightingEngine(t.Show, () => now);
        engine.Go();
        engine.ComputeFrame();

        engine.SetButton(button.Id, true);
        now = 1;
        var half = engine.ComputeFrame().Universe(1);
        Assert.Equal(100, half[4]);              // Par2 dimmer at half level
        Assert.InRange(half[1], 127, 128);       // Par1 red halfway to the button's 0

        now = 3;
        var full = engine.ComputeFrame().Universe(1);
        Assert.Equal(100, full[0]);              // Par1 dimmer: cue's 100 beats button's 50
        Assert.Equal(0, full[1]);
        Assert.Equal(200, full[3]);
        Assert.Equal(200, full[4]);

        engine.SetButton(button.Id, false);
        button.FadeOut = 0;
        now = 3.1;
        var off = engine.ComputeFrame().Universe(1);
        Assert.Equal(255, off[1]);               // back to the cue's red
        Assert.Equal(0, off[4]);
    }

    [Fact]
    public void Cue_list_effects_leave_button_controlled_fixtures_alone()
    {
        var t = new TestShow();
        var chase = t.AddPreset("Chase", (t.Par1, "Intensity", 255), (t.Par2, "Intensity", 255));
        chase.Effects.Add(new Effect { Type = EffectType.Chase, Speed = 1, Size = 1, FixtureIds = { t.Par1.Id, t.Par2.Id } });
        t.AddCue(0, chase);
        var full = t.AddPreset("Par 2 full", (t.Par2, "Intensity", 255));
        var button = AddButton(t, 0, 0, full);
        button.Mode = CueButtonMode.Flash;
        var engine = new LightingEngine(t.Show);
        engine.Go();

        Assert.Equal(0, engine.ComputeFrame(0.1).Universe(1)[4]);   // chased off
        engine.SetButton(button.Id, true);
        var u = engine.ComputeFrame(0.1).Universe(1);
        Assert.Equal(255, u[0]);   // Par1 still follows the chase
        Assert.Equal(255, u[4]);   // Par2 belongs to the button now
    }

    [Fact]
    public void Grid_is_not_limited_and_keeps_room_around_buttons()
    {
        var t = new TestShow();
        Assert.Equal((0, 3, 0, 7), t.Show.ButtonPage.Extent());   // empty page shows the starting 8 x 4

        AddButton(t, 10, 20);
        AddButton(t, -3, 1);
        Assert.Equal((-5, 12, -1, 22), t.Show.ButtonPage.Extent());
    }

    [Fact]
    public void Starting_a_cue_fades_buttons_out_and_a_toggle_button_fades_the_cue_out()
    {
        var t = new TestShow();
        var cue1 = t.AddPreset("Cue 1", (t.Par1, "Intensity", 200));
        var cue2 = t.AddPreset("Cue 2", (t.Par1, "Intensity", 100));
        t.AddCue(0, cue1);
        t.AddCue(0, cue2);
        var look = t.AddPreset("Button", (t.Par2, "Intensity", 255));
        var button = AddButton(t, 0, 0, look);
        button.FadeIn = 2;
        button.FadeOut = 1;
        double now = 0;
        var engine = new LightingEngine(t.Show, () => now);

        engine.Go();
        engine.ComputeFrame();
        engine.SetButton(button.Id, true);                 // button takes over: cue 1 fades out over 2 s
        now = 1;
        Assert.InRange(engine.ComputeFrame().Universe(1)[0], 95, 105);
        now = 2.5;
        var u = engine.ComputeFrame().Universe(1);
        Assert.Equal(0, u[0]);
        Assert.Equal(255, u[4]);

        engine.Go();                                         // continues with cue 2 and fades the button out
        Assert.False(engine.IsButtonOn(button.Id));
        now = 4;
        u = engine.ComputeFrame().Universe(1);
        Assert.Equal(100, u[0]);
        Assert.Equal(0, u[4]);
    }
}
