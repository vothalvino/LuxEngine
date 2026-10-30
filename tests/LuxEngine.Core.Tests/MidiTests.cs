using LuxEngine.Control;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Tests;

public class MidiTests
{
    private static (TestShow Show, LightingEngine Engine, Guid PresetId) Setup()
    {
        var t = new TestShow();
        var preset = t.AddPreset("Look", (t.Par1, "Intensity", 200));
        t.Show.MidiMappings.Add(new MidiMapping { MessageType = "Note", Channel = 0, Number = 60, Action = MidiActions.Preset, Target = preset.Id });
        return (t, new LightingEngine(t.Show), preset.Id);
    }

    [Fact]
    public void Preset_button_goes_through_the_app_like_a_click()
    {
        var (_, engine, presetId) = Setup();
        var midi = new MidiController(engine);
        Guid? requested = null;
        midi.PresetRequested += id => requested = id;

        midi.Handle(new MidiInput("Note", 0, 60, 100));

        Assert.Equal(presetId, requested);
        Assert.Empty(engine.ProgrammerFixtures());   // the app shows it, not the MIDI thread
    }

    [Fact]
    public void Without_the_app_listening_the_preset_is_still_shown()
    {
        var (t, engine, _) = Setup();
        new MidiController(engine).Handle(new MidiInput("Note", 0, 60, 100));
        Assert.Equal(200, engine.GetPlaybackValue(t.Par1.Id, "Intensity"));
    }
}
