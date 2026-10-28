using LuxEngine.Core.Cues;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Shows;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace LuxEngine.Control;

/// <summary>A MIDI message reduced to what mappings care about.</summary>
public readonly record struct MidiInput(string MessageType, int Channel, int Number, int Value)
{
    public string Describe() => MessageType == "Note"
        ? $"Note {Number} (channel {Channel + 1})"
        : $"Control {Number} (channel {Channel + 1})";
}

/// <summary>Things a MIDI button or fader can do.</summary>
public static class MidiActions
{
    public const string GrandMaster = "GrandMaster";
    public const string Go = "Go";
    public const string Back = "Back";
    public const string Pause = "Pause";
    public const string Release = "Release";
    public const string Blackout = "Blackout";
    public const string Preset = "Preset";
    public const string CueButton = "CueButton";

    public static readonly string[] All = { GrandMaster, Go, Back, Pause, Release, Blackout, Preset, CueButton };
}

/// <summary>Listens to all connected MIDI inputs and runs the show's MIDI mappings.</summary>
public sealed class MidiController : IDisposable
{
    private readonly LightingEngine _engine;
    private readonly List<InputDevice> _devices = new();
    private readonly Dictionary<(string, int, int), bool> _pressed = new();
    private Action<MidiInput>? _learn;

    public MidiController(LightingEngine engine) => _engine = engine;

    /// <summary>Raised for every incoming message (on a MIDI thread).</summary>
    public event Action<MidiInput>? MessageReceived;

    /// <summary>Raised after a mapping changed engine state the UI should show (e.g. blackout).</summary>
    public event Action<string>? ActionPerformed;

    public IReadOnlyList<string> DeviceNames => _devices.Select(d => d.Name).ToList();

    public static IReadOnlyList<string> AvailableDevices()
    {
        try
        {
            return InputDevice.GetAll().Select(d => { var n = d.Name; d.Dispose(); return n; }).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Opens every MIDI input device. Returns errors for devices that could not be opened.</summary>
    public IReadOnlyList<string> Start()
    {
        Stop();
        var errors = new List<string>();
        IEnumerable<InputDevice> devices;
        try
        {
            devices = InputDevice.GetAll().ToList();
        }
        catch (Exception ex)
        {
            return new[] { ex.Message };
        }

        foreach (var device in devices)
        {
            try
            {
                device.EventReceived += OnEvent;
                device.StartEventsListening();
                _devices.Add(device);
            }
            catch (Exception ex)
            {
                errors.Add($"{device.Name}: {ex.Message}");
                device.Dispose();
            }
        }
        return errors;
    }

    public void Stop()
    {
        foreach (var d in _devices)
        {
            d.EventReceived -= OnEvent;
            d.Dispose();
        }
        _devices.Clear();
    }

    /// <summary>The next message received is passed to <paramref name="callback"/> instead of running mappings.</summary>
    public void Learn(Action<MidiInput> callback) => _learn = callback;

    public void CancelLearn() => _learn = null;

    private void OnEvent(object? sender, MidiEventReceivedEventArgs e)
    {
        MidiInput? input = e.Event switch
        {
            NoteOnEvent n => new MidiInput("Note", n.Channel, n.NoteNumber, n.Velocity),
            NoteOffEvent n => new MidiInput("Note", n.Channel, n.NoteNumber, 0),
            ControlChangeEvent c => new MidiInput("ControlChange", c.Channel, c.ControlNumber, c.ControlValue),
            _ => null,
        };
        if (input is not MidiInput message) return;
        MessageReceived?.Invoke(message);

        var learn = Interlocked.Exchange(ref _learn, null);
        if (learn is not null)
        {
            if (message.Value > 0) learn(message);
            else _learn = learn; // ignore note-off while learning
            return;
        }
        Handle(message);
    }

    /// <summary>Runs every mapping that matches the message. Public for testing.</summary>
    public void Handle(MidiInput message)
    {
        var show = _engine.Show;
        var key = (message.MessageType, message.Channel, message.Number);
        bool down = message.Value >= 64 || (message.MessageType == "Note" && message.Value > 0);
        bool wasDown = _pressed.GetValueOrDefault(key);
        _pressed[key] = down;
        bool pressed = down && !wasDown;

        List<MidiMapping> mappings;
        lock (_engine.SyncRoot)
        {
            mappings = show.MidiMappings.Where(m =>
                m.MessageType == message.MessageType && m.Channel == message.Channel && m.Number == message.Number).ToList();
        }

        foreach (var mapping in mappings)
        {
            switch (mapping.Action)
            {
                case MidiActions.GrandMaster:
                    _engine.GrandMaster = message.Value / 127.0;
                    break;
                case MidiActions.Go when pressed:
                    _engine.Go();
                    break;
                case MidiActions.Back when pressed:
                    _engine.Back();
                    break;
                case MidiActions.Pause when pressed:
                    _engine.TogglePause();
                    break;
                case MidiActions.Release when pressed:
                    _engine.Release();
                    break;
                case MidiActions.Blackout when pressed:
                    _engine.Blackout = !_engine.Blackout;
                    break;
                case MidiActions.Preset when pressed && mapping.Target is Guid id:
                    var preset = show.PresetRoot.FindPreset(id);
                    if (preset is not null) _engine.ApplyPreset(preset);
                    break;
                case MidiActions.CueButton when mapping.Target is Guid buttonId:
                    var button = show.ButtonPage.Find(buttonId);
                    if (button is null) continue;
                    if (button.Mode == CueButtonMode.Flash)
                    {
                        if (down == wasDown) continue;
                        _engine.SetButton(buttonId, down);
                    }
                    else if (pressed) _engine.ToggleButton(buttonId);
                    else continue;
                    break;
                default:
                    continue;
            }
            ActionPerformed?.Invoke(mapping.Action);
        }
    }

    public void Dispose() => Stop();
}
