using System.Diagnostics;
using LuxEngine.Core.Cues;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Presets;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Engine;

/// <summary>DMX values for every universe in one output frame. Universe numbers start at 1.</summary>
public sealed class DmxFrame
{
    public Dictionary<int, byte[]> Universes { get; } = new();

    public byte[] Universe(int number)
    {
        if (!Universes.TryGetValue(number, out var data))
            Universes[number] = data = new byte[PatchHelper.UniverseSize];
        return data;
    }
}

/// <summary>What a fixture currently looks like, for drawing it on screen.</summary>
public readonly record struct FixtureLook(double Red, double Green, double Blue, double Intensity, double? Pan, double? Tilt);

/// <summary>
/// The heart of LuxEngine. Combines the running cue, the programmer and effects into final DMX values:
/// <c>defaults → cue → programmer → effects → grand master → DMX</c>.
/// All public members are thread-safe; the output thread calls <see cref="ComputeFrame()"/>.
/// </summary>
public sealed class LightingEngine
{
    private readonly object _sync = new();
    private readonly Func<double> _clock;
    private Show _show;
    private bool _patchDirty = true;
    private Dictionary<Guid, PatchedFixture> _fixtures = new();
    private Dictionary<ChannelKey, PatchedChannel> _channels = new();
    private Dictionary<ChannelKey, double> _lastValues = new();
    private Dictionary<ChannelKey, double> _buttonValues = new();
    private double _grandMaster = 1;

    /// <param name="clock">Time source in seconds; defaults to a real monotonic clock.</param>
    public LightingEngine(Show? show = null, Func<double>? clock = null)
    {
        _show = show ?? Show.CreateNew();
        if (clock is null)
        {
            var stopwatch = Stopwatch.StartNew();
            clock = () => stopwatch.Elapsed.TotalSeconds;
        }
        _clock = clock;
    }

    /// <summary>Lock this while changing the show from the UI so the output thread never sees half an edit.</summary>
    public object SyncRoot => _sync;

    public Show Show { get { lock (_sync) return _show; } }
    public Programmer Programmer { get; } = new();
    public CuePlayer CuePlayer { get; } = new();
    public ButtonPlayer Buttons { get; } = new();

    /// <summary>Overall brightness, 0–1.</summary>
    public double GrandMaster
    {
        get => _grandMaster;
        set => _grandMaster = Math.Clamp(value, 0, 1);
    }

    public bool Blackout { get; set; }

    /// <summary>Live audio level 0–1 from the sound-to-light input.</summary>
    public double SoundLevel { get; set; }

    /// <summary>How strongly the audio level dims the lights, 0 (off) to 1.</summary>
    public double SoundToLightAmount { get; set; }

    /// <summary>Monotonic time in seconds used for fades and effects.</summary>
    public double Now => _clock();

    /// <summary>Channel values (0–255) from the most recent frame.</summary>
    public IReadOnlyDictionary<ChannelKey, double> LastValues => _lastValues;

    public void LoadShow(Show show)
    {
        lock (_sync)
        {
            _show = show;
            Programmer.Clear();
            CuePlayer.Reset();
            Buttons.Reset();
            _buttonValues = new();
            _patchDirty = true;
        }
    }

    /// <summary>Runs an edit of the show under the engine lock and refreshes the patch afterwards.</summary>
    public void Edit(Action<Show> edit)
    {
        lock (_sync)
        {
            edit(_show);
            _patchDirty = true;
        }
    }

    public void InvalidatePatch()
    {
        lock (_sync) _patchDirty = true;
    }

    public IReadOnlyDictionary<Guid, PatchedFixture> Fixtures
    {
        get { lock (_sync) { EnsurePatch(); return _fixtures; } }
    }

    // ---------------------------------------------------------------- Programmer helpers

    public void SetValue(Guid fixtureId, string channel, byte value)
    {
        lock (_sync) Programmer.Set(fixtureId, channel, value);
    }

    /// <summary>Sets every channel of the given type on the given fixtures.</summary>
    public void SetValue(IEnumerable<Guid> fixtureIds, ChannelType type, byte value)
    {
        lock (_sync)
        {
            EnsurePatch();
            foreach (var id in fixtureIds)
                if (_fixtures.TryGetValue(id, out var f))
                    foreach (var ch in f.Channels.Where(c => c.Channel.Type == type))
                        Programmer.Set(id, ch.Channel.Name, value);
        }
    }

    public void ApplyPreset(Preset preset)
    {
        lock (_sync) Programmer.Apply(preset);
    }

    public void AddEffect(Effect effect)
    {
        lock (_sync) Programmer.AddEffect(effect);
    }

    public void ClearProgrammer()
    {
        lock (_sync) Programmer.Clear();
    }

    public Preset RecordPreset(string name, IReadOnlyCollection<Guid>? fixtureIds = null)
    {
        lock (_sync) return Programmer.ToPreset(name, fixtureIds);
    }

    /// <summary>
    /// The value a channel is set to before effects and the master: the programmer value if you set one,
    /// otherwise what the running cue gives it, otherwise its default. Null for unknown channels.
    /// </summary>
    public double? GetPlaybackValue(Guid fixtureId, string channel)
    {
        lock (_sync)
        {
            EnsurePatch();
            var key = new ChannelKey(fixtureId, channel);
            if (Programmer.Values.TryGetValue(key, out var programmed)) return programmed;
            if (_buttonValues.TryGetValue(key, out var button)) return button;
            if (CuePlayer.Current.TryGetValue(key, out var cue)) return cue;
            return _channels.TryGetValue(key, out var ch) ? ch.Channel.Default : null;
        }
    }

    /// <summary>The current output value of a channel (what is on stage), or null if unknown.</summary>
    public double? GetOutput(Guid fixtureId, string channel) =>
        _lastValues.TryGetValue(new ChannelKey(fixtureId, channel), out var v) ? v : null;

    // ---------------------------------------------------------------- Cue playback

    public CueList CueList { get { lock (_sync) return _show.CueList; } }

    /// <summary>Starts the next cue (or the first one when nothing is running).</summary>
    public void Go()
    {
        lock (_sync)
        {
            if (CuePlayer.IsPaused) { CuePlayer.Resume(); return; }
            int next = CuePlayer.CurrentIndex + 1;
            if (next >= _show.CueList.Cues.Count)
            {
                if (!_show.CueList.Loop || _show.CueList.Cues.Count == 0) return;
                next = 0;
            }
            GoToLocked(next);
        }
    }

    public void Back()
    {
        lock (_sync)
        {
            if (CuePlayer.CurrentIndex > 0) GoToLocked(CuePlayer.CurrentIndex - 1);
        }
    }

    public void GoTo(int index)
    {
        lock (_sync)
        {
            if (index >= 0 && index < _show.CueList.Cues.Count) GoToLocked(index);
        }
    }

    public void TogglePause()
    {
        lock (_sync)
        {
            if (CuePlayer.IsPaused) CuePlayer.Resume();
            else CuePlayer.Pause();
        }
    }

    /// <summary>Fades everything played back by cues to dark and stops the cue list.</summary>
    public void Release(double fadeSeconds = 2)
    {
        lock (_sync)
        {
            EnsurePatch();
            CuePlayer.Tick(Now);
            var target = CuePlayer.Current.Keys
                .Where(k => _channels.TryGetValue(k, out var c) && c.Channel.Type.IsIntensity())
                .ToDictionary(k => k, _ => 0.0);
            var timing = new Cue { FadeIn = fadeSeconds, FadeOut = fadeSeconds };
            CuePlayer.Start(-1, null, timing, target, Describe, Array.Empty<Effect>());
        }
    }

    // ---------------------------------------------------------------- Cue buttons

    public bool IsButtonOn(Guid id)
    {
        lock (_sync) return Buttons.IsOn(id);
    }

    public double ButtonLevel(Guid id)
    {
        lock (_sync) return Buttons.Level(id);
    }

    /// <summary>Switches a cue button; switching one on turns off the buttons that touch it.</summary>
    public void SetButton(Guid id, bool on)
    {
        lock (_sync)
        {
            var page = _show.ButtonPage;
            var button = page.Find(id);
            if (button is null) return;
            Buttons.Tick(Now, page.Find);
            if (on)
                foreach (var other in page.GroupOf(button))
                    if (other.Id != id) Buttons.Set(other.Id, false);
            Buttons.Set(id, on);
        }
    }

    public void ToggleButton(Guid id)
    {
        lock (_sync) SetButton(id, !Buttons.IsOn(id));
    }

    public void ReleaseButtons()
    {
        lock (_sync) Buttons.ReleaseAll();
    }

    /// <summary>
    /// Lays the active buttons over the cue list values. Dimmers take the highest value (scaled by the
    /// button's level); other channels crossfade toward the button's value, the newest button winning.
    /// </summary>
    private List<Effect> ApplyButtons(Dictionary<ChannelKey, double> values)
    {
        var effects = new List<Effect>();
        var changed = new Dictionary<ChannelKey, double>();
        var page = _show.ButtonPage;
        foreach (var (id, level) in Buttons.Active)
        {
            var button = page.Find(id);
            if (button is null) continue;
            var presets = button.PresetIds.Select(_show.PresetRoot.FindPreset).OfType<Preset>().ToList();
            foreach (var (key, target) in BuildCueTarget(new Dictionary<ChannelKey, double>(), presets))
            {
                if (!values.TryGetValue(key, out var below)) continue;
                var type = _channels[key].Channel.Type;
                double v = type.IsIntensity() ? Math.Max(below, target * level)
                    : type.Snaps() ? target
                    : below + (target - below) * level;
                values[key] = changed[key] = v;
            }
            effects.AddRange(presets.SelectMany(p => p.Effects));
        }
        _buttonValues = changed;
        return effects;
    }

    private void GoToLocked(int index)
    {
        EnsurePatch();
        CuePlayer.Tick(Now);
        var cue = _show.CueList.Cues[index];
        var presets = cue.PresetIds.Select(_show.PresetRoot.FindPreset).OfType<Preset>().ToList();
        var target = BuildCueTarget(CuePlayer.Current, presets);
        var effects = presets.SelectMany(p => p.Effects).ToList();
        CuePlayer.Start(index, cue, cue, target, Describe, effects);
    }

    /// <summary>
    /// What the stage should look like after a cue: intensities not in the cue go to zero, other
    /// channels keep their value. Within the cue, dimmers take the highest value of any preset and
    /// everything else takes the value from the last preset that sets it.
    /// </summary>
    internal Dictionary<ChannelKey, double> BuildCueTarget(IReadOnlyDictionary<ChannelKey, double> current, IEnumerable<Preset> presets)
    {
        var target = new Dictionary<ChannelKey, double>();
        foreach (var key in current.Keys)
            if (_channels.TryGetValue(key, out var c) && c.Channel.Type.IsIntensity()) target[key] = 0;

        var fromPresets = new HashSet<ChannelKey>();
        foreach (var preset in presets)
        foreach (var v in preset.Values)
        {
            var key = new ChannelKey(v.FixtureId, v.Channel);
            if (!_channels.TryGetValue(key, out var channel)) continue;
            if (channel.Channel.Type.IsIntensity() && fromPresets.Contains(key))
                target[key] = Math.Max(target[key], v.Value);
            else
                target[key] = v.Value;
            fromPresets.Add(key);
        }
        return target;
    }

    private (bool intensity, bool snaps, double defaultValue) Describe(ChannelKey key) =>
        _channels.TryGetValue(key, out var c)
            ? (c.Channel.Type.IsIntensity(), c.Channel.Type.Snaps(), c.Channel.Default)
            : (false, false, 0);

    // ---------------------------------------------------------------- Frame computation

    public DmxFrame ComputeFrame() => ComputeFrame(Now);

    public DmxFrame ComputeFrame(double now)
    {
        lock (_sync)
        {
            EnsurePatch();
            CuePlayer.Tick(now);
            Buttons.Tick(now, _show.ButtonPage.Find);
            HandleAutoFollow();

            var values = new Dictionary<ChannelKey, double>(_channels.Count);
            foreach (var (key, ch) in _channels) values[key] = ch.Channel.Default;
            foreach (var (key, v) in CuePlayer.Current)
                if (values.ContainsKey(key)) values[key] = v;
            var buttonEffects = ApplyButtons(values);
            foreach (var (key, v) in Programmer.Values)
                if (values.ContainsKey(key)) values[key] = v;

            // Effects from the running cue leave alone whatever you are controlling by hand;
            // effects you start yourself always apply.
            var brightness = new Dictionary<Guid, double>();
            // Cue list effects also leave alone what a cue button controls.
            var handControlled = ControlledFixtures(Programmer.Values.Keys);
            var overCueList = ControlledFixtures(Programmer.Values.Keys.Concat(_buttonValues.Keys));
            foreach (var effect in CuePlayer.ActiveEffects)
                EffectEngine.Apply(effect, now, _fixtures, values, brightness,
                    overCueList.GetValueOrDefault(EffectEngine.Affects(effect.Type)));
            foreach (var effect in buttonEffects)
                EffectEngine.Apply(effect, now, _fixtures, values, brightness,
                    handControlled.GetValueOrDefault(EffectEngine.Affects(effect.Type)));
            foreach (var effect in Programmer.Effects)
                EffectEngine.Apply(effect, now, _fixtures, values, brightness);

            double master = Blackout ? 0 : GrandMaster;
            if (SoundToLightAmount > 0)
                master *= 1 - Math.Clamp(SoundToLightAmount, 0, 1) * (1 - Math.Clamp(SoundLevel, 0, 1));

            var frame = new DmxFrame();
            foreach (var fixture in _fixtures.Values)
            {
                double scale = master * brightness.GetValueOrDefault(fixture.Instance.Id, 1);
                var data = frame.Universe(fixture.Instance.Universe);
                foreach (var ch in fixture.Channels)
                {
                    var key = new ChannelKey(fixture.Instance.Id, ch.Channel.Name);
                    double v = values[key];
                    var type = ch.Channel.Type;
                    if (type.IsIntensity() || (!fixture.HasDimmer && type.IsAdditiveColor()))
                        v *= scale;
                    v = Math.Clamp(v, 0, 255);
                    values[key] = v;
                    if (ch.Address is >= 1 and <= PatchHelper.UniverseSize)
                        data[ch.Address - 1] = (byte)Math.Round(v);
                }
            }

            _lastValues = values;
            return frame;
        }
    }

    /// <summary>Fixtures that have any of the given channels set, grouped by what effects would change ("Dimmer", "Color", "Position").</summary>
    private Dictionary<string, HashSet<Guid>> ControlledFixtures(IEnumerable<ChannelKey> keys)
    {
        var result = new Dictionary<string, HashSet<Guid>>();
        foreach (var key in keys)
        {
            if (!_channels.TryGetValue(key, out var ch) || !_fixtures.TryGetValue(key.FixtureId, out var fixture)) continue;
            var type = ch.Channel.Type;
            string? group = type switch
            {
                _ when type.IsIntensity() => "Dimmer",
                _ when type.IsColorMix() => "Color",
                ChannelType.Pan or ChannelType.Tilt => "Position",
                _ => null,
            };
            if (group is null) continue;
            Add(group, key.FixtureId);
            // Without a dimmer channel, brightness is controlled through the colors.
            if (group == "Color" && !fixture.HasDimmer) Add("Dimmer", key.FixtureId);
        }
        return result;

        void Add(string group, Guid id)
        {
            if (!result.TryGetValue(group, out var set)) result[group] = set = new HashSet<Guid>();
            set.Add(id);
        }
    }

    private void HandleAutoFollow()
    {
        var cue = CuePlayer.CurrentCue;
        if (cue is null || !cue.AutoFollow || CuePlayer.IsPaused) return;
        if (CuePlayer.Elapsed < CuePlayer.Duration + cue.FollowDelay) return;
        int next = CuePlayer.CurrentIndex + 1;
        if (next >= _show.CueList.Cues.Count)
        {
            if (!_show.CueList.Loop) return;
            next = 0;
        }
        GoToLocked(next);
    }

    private void EnsurePatch()
    {
        if (!_patchDirty) return;
        _fixtures = _show.Patched().ToDictionary(p => p.Instance.Id);
        _channels = new Dictionary<ChannelKey, PatchedChannel>();
        foreach (var f in _fixtures.Values)
        foreach (var ch in f.Channels)
            _channels[new ChannelKey(f.Instance.Id, ch.Channel.Name)] = ch;
        _patchDirty = false;
    }

    /// <summary>The fixture's current color and brightness for the stage view.</summary>
    public FixtureLook GetLook(Guid fixtureId)
    {
        PatchedFixture? fixture;
        lock (_sync)
        {
            EnsurePatch();
            if (!_fixtures.TryGetValue(fixtureId, out fixture)) return default;
        }
        var values = _lastValues;
        double Get(ChannelType type, double fallback)
        {
            var ch = fixture.Channels.FirstOrDefault(c => c.Channel.Type == type);
            return ch.Channel is not null && values.TryGetValue(new ChannelKey(fixtureId, ch.Channel.Name), out var v) ? v / 255 : fallback;
        }

        bool rgb = fixture.Has(ChannelType.Red) || fixture.Has(ChannelType.Green) || fixture.Has(ChannelType.Blue);
        bool cmy = fixture.Has(ChannelType.Cyan) || fixture.Has(ChannelType.Magenta) || fixture.Has(ChannelType.Yellow);
        double r = 1, g = 1, b = 1;
        if (rgb)
        {
            double w = Math.Max(Get(ChannelType.White, 0), Math.Max(Get(ChannelType.WarmWhite, 0), Get(ChannelType.ColdWhite, 0)));
            double amber = Get(ChannelType.Amber, 0), uv = Get(ChannelType.UV, 0);
            r = Math.Min(1, Get(ChannelType.Red, 0) + w + amber + uv * 0.4);
            g = Math.Min(1, Get(ChannelType.Green, 0) + w + amber * 0.6);
            b = Math.Min(1, Get(ChannelType.Blue, 0) + w + uv);
        }
        else if (cmy)
        {
            r = 1 - Get(ChannelType.Cyan, 0);
            g = 1 - Get(ChannelType.Magenta, 0);
            b = 1 - Get(ChannelType.Yellow, 0);
        }

        double intensity = fixture.HasDimmer ? Get(ChannelType.Intensity, 0) : Math.Max(r, Math.Max(g, b));
        if (!fixture.HasDimmer && intensity > 0) { r /= intensity; g /= intensity; b /= intensity; }
        double? pan = fixture.Has(ChannelType.Pan) ? Get(ChannelType.Pan, 0.5) : null;
        double? tilt = fixture.Has(ChannelType.Tilt) ? Get(ChannelType.Tilt, 0.5) : null;
        return new FixtureLook(r, g, b, intensity, pan, tilt);
    }
}
