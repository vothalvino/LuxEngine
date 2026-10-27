using LuxEngine.Core.Cues;
using LuxEngine.Core.Effects;

namespace LuxEngine.Core.Engine;

/// <summary>Crossfades from the look on stage to the look of a cue.</summary>
public sealed class CuePlayer
{
    private Dictionary<ChannelKey, double> _from = new();
    private Dictionary<ChannelKey, double> _to = new();
    private Dictionary<ChannelKey, double> _current = new();
    private HashSet<ChannelKey> _intensityKeys = new();
    private HashSet<ChannelKey> _snapKeys = new();
    private double _elapsed;
    private double? _lastTick;
    private Cue? _timing;

    /// <summary>Index of the cue on stage, or -1 when nothing is running.</summary>
    public int CurrentIndex { get; private set; } = -1;

    public Cue? CurrentCue { get; private set; }
    public bool IsPaused { get; private set; }
    public IReadOnlyList<Effect> ActiveEffects { get; private set; } = Array.Empty<Effect>();
    public IReadOnlyDictionary<ChannelKey, double> Current => _current;

    /// <summary>Total length of the running transition in seconds (delay + longest fade).</summary>
    public double Duration => _timing is null ? 0 : _timing.Delay + Math.Max(_timing.FadeIn, _timing.FadeOut);

    public double Elapsed => _elapsed;

    /// <summary>0–1 progress of the running transition.</summary>
    public double Progress => Duration <= 0 ? 1 : Math.Clamp(_elapsed / Duration, 0, 1);

    public bool IsFading => _timing is not null && _elapsed < Duration;

    /// <summary>Starts fading to a new target. Keys missing from <paramref name="target"/> stay at their current value.</summary>
    public void Start(int index, Cue? cue, Cue timing, Dictionary<ChannelKey, double> target,
        Func<ChannelKey, (bool intensity, bool snaps, double defaultValue)> describe,
        IReadOnlyList<Effect> effects)
    {
        var from = new Dictionary<ChannelKey, double>(_current);
        foreach (var key in target.Keys)
            if (!from.ContainsKey(key)) from[key] = describe(key).defaultValue;

        _intensityKeys = new HashSet<ChannelKey>();
        _snapKeys = new HashSet<ChannelKey>();
        foreach (var key in from.Keys)
        {
            var (intensity, snaps, _) = describe(key);
            if (intensity) _intensityKeys.Add(key);
            if (snaps) _snapKeys.Add(key);
        }

        _from = from;
        _to = new Dictionary<ChannelKey, double>(from);
        foreach (var (key, value) in target) _to[key] = value;

        _timing = timing;
        CurrentIndex = index;
        CurrentCue = cue;
        ActiveEffects = effects;
        _elapsed = 0;
        IsPaused = false;
        Evaluate();
    }

    /// <summary>Advances time. Call once per frame with a monotonic clock in seconds.</summary>
    public void Tick(double now)
    {
        if (_lastTick is double last && !IsPaused) _elapsed += Math.Max(0, now - last);
        _lastTick = now;
        Evaluate();
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    /// <summary>Jumps to the end of the current fade.</summary>
    public void Complete()
    {
        _elapsed = Duration;
        IsPaused = false;
        Evaluate();
    }

    /// <summary>Clears everything immediately.</summary>
    public void Reset()
    {
        _from.Clear();
        _to.Clear();
        _current = new();
        _timing = null;
        CurrentIndex = -1;
        CurrentCue = null;
        ActiveEffects = Array.Empty<Effect>();
        IsPaused = false;
    }

    private void Evaluate()
    {
        if (_timing is null) return;
        double t = _elapsed - _timing.Delay;
        var current = new Dictionary<ChannelKey, double>(_to.Count);
        foreach (var (key, to) in _to)
        {
            double from = _from.GetValueOrDefault(key, to);
            if (from == to) { current[key] = to; continue; }

            if (_snapKeys.Contains(key))
            {
                current[key] = t >= 0 ? to : from;
                continue;
            }

            double fade = _intensityKeys.Contains(key) && to < from ? _timing.FadeOut : _timing.FadeIn;
            double p = fade <= 0 ? (t >= 0 ? 1 : 0) : Math.Clamp(t / fade, 0, 1);
            p = p * p * (3 - 2 * p); // smoothstep: soft start and end
            current[key] = from + (to - from) * p;
        }
        _current = current;
    }
}
