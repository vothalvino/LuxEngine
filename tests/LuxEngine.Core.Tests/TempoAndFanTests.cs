using LuxEngine.Core.Effects;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Shows;
using Xunit;

namespace LuxEngine.Core.Tests;

public class TempoAndFanTests
{
    [Fact]
    public void Tapping_gives_the_average_tempo()
    {
        var tap = new TapTempo();
        Assert.Null(tap.Tap(10));
        Assert.Equal(120, tap.Tap(10.5)!.Value, 3);
        Assert.Equal(120, tap.Tap(11.0)!.Value, 3);
        Assert.Equal(100, tap.Tap(11.8)!.Value, 3);   // 1.8 s over 3 gaps
    }

    [Fact]
    public void A_pause_starts_a_new_count()
    {
        var tap = new TapTempo();
        tap.Tap(0);
        tap.Tap(0.5);
        Assert.Null(tap.Tap(5));
        Assert.Equal(1, tap.Count);
        Assert.Equal(60, tap.Tap(6)!.Value, 3);
    }

    [Fact]
    public void Engine_tap_sets_the_tempo_and_puts_the_beat_on_the_tap()
    {
        double now = 0;
        var engine = new LightingEngine(clock: () => now);
        Assert.Equal(120, engine.Bpm);
        now = 3; engine.Tap();
        now = 3.6; Assert.Equal(100, engine.Tap(), 3);
        Assert.Equal(0, engine.BeatPhase, 3);
        now = 3.9; Assert.Equal(0.5, engine.BeatPhase, 3);
    }

    [Fact]
    public void Changing_the_bpm_keeps_the_place_in_the_beat()
    {
        double now = 10.125;
        var engine = new LightingEngine(clock: () => now);
        double before = engine.BeatPhase;
        engine.Bpm = 90;
        Assert.Equal(90, engine.Bpm);
        Assert.Equal(before, engine.BeatPhase, 6);
        engine.Bpm = 5000;
        Assert.Equal(Tempo.MaxBpm, engine.Bpm);
    }

    [Fact]
    public void A_synced_chase_steps_with_the_beat()
    {
        var test = new TestShow();
        double now = 0;
        var engine = new LightingEngine(test.Show, () => now);
        engine.SetValue(new[] { test.Par1.Id, test.Par2.Id }, ChannelType.Intensity, 255);
        // 2 lights, 2 beats per cycle at 120 BPM: each light is on for one beat (0.5 s). Speed is ignored.
        engine.AddEffect(new Effect { Type = EffectType.Chase, Sync = true, Beats = 2, Speed = 7.3, FixtureIds = { test.Par1.Id, test.Par2.Id } });

        byte Dimmer(FixtureInstance f) => engine.ComputeFrame(now).Universe(f.Universe)[f.Address - 1];
        now = 0.25; Assert.Equal(255, Dimmer(test.Par1)); Assert.Equal(0, Dimmer(test.Par2));
        now = 0.75; Assert.Equal(0, Dimmer(test.Par1)); Assert.Equal(255, Dimmer(test.Par2));
        now = 1.25; Assert.Equal(255, Dimmer(test.Par1));

        // Twice the tempo: twice as fast.
        now = 2; engine.Tap();
        now = 2.25; engine.Tap();
        Assert.Equal(240, engine.Bpm, 3);
        now = 2.30; Assert.Equal(255, Dimmer(test.Par1));
        now = 2.60; Assert.Equal(255, Dimmer(test.Par2));
    }

    [Fact]
    public void Sync_and_beats_are_saved_and_cloned()
    {
        var effect = new Effect { Type = EffectType.Wave, Sync = true, Beats = 8 };
        var clone = effect.Clone();
        Assert.True(clone.Sync);
        Assert.Equal(8, clone.Beats);

        var test = new TestShow();
        var preset = test.AddPreset("Wave");
        preset.Effects.Add(effect);
        var loaded = ShowSerializer.Deserialize(ShowSerializer.Serialize(test.Show));
        var saved = loaded.PresetRoot.Presets[0].Effects[0];
        Assert.True(saved.Sync);
        Assert.Equal(8, saved.Beats);
    }

    [Fact]
    public void Fan_spreads_evenly()
    {
        Assert.Equal(new[] { 0.0, 50, 100 }, Fan.Spread(3, 0, 100));
        Assert.Equal(new[] { 200.0, 100 }, Fan.Spread(2, 200, 100));
        Assert.Equal(new[] { 50.0 }, Fan.Spread(1, 0, 100));
        Assert.Empty(Fan.Spread(0, 0, 100));
    }

    [Fact]
    public void Fan_follows_the_stage_order_and_skips_lights_without_the_channel()
    {
        var test = new TestShow();
        var engine = new LightingEngine(test.Show);
        // Selected back to front; the fan still runs left to right as the lights stand.
        engine.Fan(new[] { test.NoDimmer.Id, test.Par2.Id, test.Par1.Id }, ChannelType.Intensity, 0, 255);
        var order = new[] { test.Par1, test.Par2 }.OrderByStage().ToList();
        Assert.Equal(0, engine.Programmer.Values[new ChannelKey(order[0].Id, "Intensity")]);
        Assert.Equal(255, engine.Programmer.Values[new ChannelKey(order[1].Id, "Intensity")]);
        Assert.DoesNotContain(engine.Programmer.Values.Keys, k => k.FixtureId == test.NoDimmer.Id);
    }
}
