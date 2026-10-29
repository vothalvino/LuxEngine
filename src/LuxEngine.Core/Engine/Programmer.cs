using LuxEngine.Core.Effects;
using LuxEngine.Core.Presets;

namespace LuxEngine.Core.Engine;

/// <summary>A saved copy of the programmer, to put things back after editing a preset.</summary>
public sealed record ProgrammerSnapshot(IReadOnlyDictionary<ChannelKey, byte> Values, IReadOnlyList<Effect> Effects);

/// <summary>
/// Values set by hand in the Control view, or by clicking a preset. The programmer has the
/// highest priority: its values override whatever the running cue is doing until it is cleared.
/// </summary>
public sealed class Programmer
{
    private readonly Dictionary<ChannelKey, byte> _values = new();
    private readonly List<Effect> _effects = new();

    public IReadOnlyDictionary<ChannelKey, byte> Values => _values;
    public IReadOnlyList<Effect> Effects => _effects;
    public bool IsEmpty => _values.Count == 0 && _effects.Count == 0;

    public void Set(Guid fixtureId, string channel, byte value) => _values[new ChannelKey(fixtureId, channel)] = value;

    public bool TryGet(Guid fixtureId, string channel, out byte value) =>
        _values.TryGetValue(new ChannelKey(fixtureId, channel), out value);

    public void Release(Guid fixtureId, string channel) => _values.Remove(new ChannelKey(fixtureId, channel));

    /// <summary>Copies a preset into the programmer. Effects of the same type on the same fixtures are replaced.</summary>
    public void Apply(Preset preset)
    {
        foreach (var v in preset.Values) Set(v.FixtureId, v.Channel, v.Value);
        foreach (var effect in preset.Effects)
        {
            _effects.RemoveAll(e => e.Type == effect.Type && e.FixtureIds.Intersect(effect.FixtureIds).Any());
            _effects.Add(effect.Clone());
        }
    }

    /// <summary>Starts an effect. It replaces an effect of the same type on the same lights; a movement replaces any other movement.</summary>
    public void AddEffect(Effect effect)
    {
        _effects.RemoveAll(e => (e.Type == effect.Type || (e.Type.IsMovement() && effect.Type.IsMovement()))
                                && e.FixtureIds.Intersect(effect.FixtureIds).Any());
        _effects.Add(effect);
    }

    public void RemoveEffect(Guid effectId) => _effects.RemoveAll(e => e.Id == effectId);

    public void ClearEffects() => _effects.Clear();

    public void ReleaseFixture(Guid fixtureId)
    {
        foreach (var key in _values.Keys.Where(k => k.FixtureId == fixtureId).ToList()) _values.Remove(key);
        foreach (var effect in _effects) effect.FixtureIds.Remove(fixtureId);
        _effects.RemoveAll(e => e.FixtureIds.Count == 0);
    }

    /// <summary>Every fixture with a value or effect in the programmer.</summary>
    public HashSet<Guid> FixtureIds() =>
        _values.Keys.Select(k => k.FixtureId).Concat(_effects.SelectMany(e => e.FixtureIds)).ToHashSet();

    public ProgrammerSnapshot Snapshot() =>
        new(new Dictionary<ChannelKey, byte>(_values), _effects.Select(e => e.Copy()).ToList());

    public void Restore(ProgrammerSnapshot snapshot)
    {
        Clear();
        foreach (var (key, value) in snapshot.Values) _values[key] = value;
        _effects.AddRange(snapshot.Effects.Select(e => e.Clone()));
    }

    public void Clear()
    {
        _values.Clear();
        _effects.Clear();
    }

    /// <summary>Turns the programmer contents for the given fixtures into a preset.</summary>
    public Preset ToPreset(string name, IReadOnlyCollection<Guid>? onlyFixtures = null)
    {
        bool Include(Guid id) => onlyFixtures is null || onlyFixtures.Count == 0 || onlyFixtures.Contains(id);
        var preset = new Preset { Name = name };
        preset.Values.AddRange(_values
            .Where(kv => Include(kv.Key.FixtureId))
            .Select(kv => new PresetValue(kv.Key.FixtureId, kv.Key.Channel, kv.Value)));
        foreach (var effect in _effects)
        {
            var copy = effect.Clone();
            copy.FixtureIds.RemoveAll(id => !Include(id));
            if (copy.FixtureIds.Count > 0) preset.Effects.Add(copy);
        }
        return preset;
    }
}
