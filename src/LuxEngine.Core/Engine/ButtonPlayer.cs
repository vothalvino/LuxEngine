using LuxEngine.Core.Cues;

namespace LuxEngine.Core.Engine;

/// <summary>Tracks which cue buttons are on and fades each one's level between 0 and 1.</summary>
public sealed class ButtonPlayer
{
    private sealed class State
    {
        public bool On;
        public double Level;
        public long Order;
    }

    private readonly Dictionary<Guid, State> _states = new();
    private double? _lastTick;
    private long _order;

    public bool IsOn(Guid id) => _states.TryGetValue(id, out var s) && s.On;

    public double Level(Guid id) => _states.TryGetValue(id, out var s) ? s.Level : 0;

    /// <summary>Buttons that are visible on stage (level above 0), oldest first, so the last one switched on wins.</summary>
    public IEnumerable<(Guid Id, double Level)> Active =>
        _states.Where(p => p.Value.Level > 0).OrderBy(p => p.Value.Order).Select(p => (p.Key, p.Value.Level));

    public void Set(Guid id, bool on)
    {
        if (!_states.TryGetValue(id, out var s)) _states[id] = s = new State();
        if (on && !s.On) s.Order = ++_order;
        s.On = on;
    }

    public void ReleaseAll()
    {
        foreach (var s in _states.Values) s.On = false;
    }

    public void Reset()
    {
        _states.Clear();
        _lastTick = null;
    }

    /// <summary>Moves each level toward on/off using the button's fade time.</summary>
    public void Tick(double now, Func<Guid, CueButton?> find)
    {
        double dt = _lastTick is double last ? Math.Max(0, now - last) : 0;
        _lastTick = now;
        foreach (var (id, s) in _states.ToList())
        {
            var button = find(id);
            if (button is null) { _states.Remove(id); continue; }
            double target = s.On ? 1 : 0;
            double fade = s.On ? button.FadeIn : button.FadeOut;
            double step = fade <= 0 ? 1 : dt / fade;
            s.Level = target > s.Level ? Math.Min(target, s.Level + step) : Math.Max(target, s.Level - step);
            if (!s.On && s.Level <= 0) _states.Remove(id);
        }
    }
}
