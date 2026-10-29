using LuxEngine.Core.Engine;
using LuxEngine.Core.Fixtures;

namespace LuxEngine.Core.Effects;

/// <summary>Calculates what a running effect does to channel values at a point in time.</summary>
public static class EffectEngine
{
    /// <param name="values">Channel values (0–255) to modify in place.</param>
    /// <param name="brightness">Per-fixture brightness multipliers, combined by multiplication.</param>
    /// <param name="exclude">Fixtures the effect should leave alone (they keep their place in the pattern).</param>
    public static void Apply(Effect effect, double time, IReadOnlyDictionary<Guid, PatchedFixture> fixtures,
        Dictionary<ChannelKey, double> values, Dictionary<Guid, double> brightness, IReadOnlySet<Guid>? exclude = null)
    {
        // Effects run across the stage as the lights stand: left to right, then top to bottom,
        // no matter in which order the lights were selected.
        var ids = effect.FixtureIds.Where(fixtures.ContainsKey)
            .OrderBy(id => Math.Round(fixtures[id].Instance.StageX, 3))
            .ThenBy(id => Math.Round(fixtures[id].Instance.StageY, 3))
            .ToList();
        int n = ids.Count;
        if (n == 0) return;
        double size = Math.Clamp(effect.Size, 0, 1);
        double cycle = time * effect.Speed;

        for (int i = 0; i < n; i++)
        {
            if (exclude is not null && exclude.Contains(ids[i])) continue;
            var fixture = fixtures[ids[i]];
            double phase = Frac(cycle + effect.Spread * i / n);

            switch (effect.Type)
            {
                case EffectType.Rainbow:
                    var (r, g, b) = HsvToRgb(phase, 1, 1);
                    foreach (var ch in fixture.Channels)
                    {
                        double? target = ch.Channel.Type switch
                        {
                            ChannelType.Red => r, ChannelType.Green => g, ChannelType.Blue => b,
                            ChannelType.Cyan => 1 - r, ChannelType.Magenta => 1 - g, ChannelType.Yellow => 1 - b,
                            ChannelType.White or ChannelType.WarmWhite or ChannelType.ColdWhite or ChannelType.Amber
                                or ChannelType.Lime or ChannelType.UV or ChannelType.Indigo => 0,
                            _ => null,
                        };
                        if (target is null) continue;
                        var key = new ChannelKey(fixture.Instance.Id, ch.Channel.Name);
                        double current = values.GetValueOrDefault(key);
                        values[key] = current + (target.Value * 255 - current) * size;
                    }
                    break;

                case EffectType.Chase:
                    int active = (int)Math.Floor(Frac(cycle) * n);
                    Multiply(brightness, fixture.Instance.Id, i == active ? 1 : 1 - size);
                    break;

                case EffectType.Wave:
                    Multiply(brightness, fixture.Instance.Id, 1 - size * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)));
                    break;

                case EffectType.Strobe:
                    Multiply(brightness, fixture.Instance.Id, phase < 0.2 ? 1 : 1 - size);
                    break;

                case EffectType.Circle:
                    double angle = 2 * Math.PI * phase;
                    Offset(values, fixture, ChannelType.Pan, size * 64 * Math.Cos(angle));
                    Offset(values, fixture, ChannelType.Tilt, size * 64 * Math.Sin(angle));
                    break;
            }
        }
    }

    /// <summary>Which kind of channels an effect changes.</summary>
    public static string Affects(EffectType type) => type switch
    {
        EffectType.Rainbow => "Color",
        EffectType.Circle => "Position",
        _ => "Dimmer",
    };

    private static void Multiply(Dictionary<Guid, double> brightness, Guid id, double factor) =>
        brightness[id] = brightness.GetValueOrDefault(id, 1) * factor;

    private static void Offset(Dictionary<ChannelKey, double> values, PatchedFixture fixture, ChannelType type, double amount)
    {
        foreach (var ch in fixture.Channels.Where(c => c.Channel.Type == type))
        {
            var key = new ChannelKey(fixture.Instance.Id, ch.Channel.Name);
            double center = values.TryGetValue(key, out var v) ? v : 128;
            values[key] = Math.Clamp(center + amount, 0, 255);
        }
    }

    private static double Frac(double x) => x - Math.Floor(x);

    /// <summary>Hue, saturation and value in 0–1 to RGB in 0–1.</summary>
    public static (double r, double g, double b) HsvToRgb(double h, double s, double v)
    {
        h = Frac(h) * 6;
        int sector = (int)Math.Floor(h) % 6;
        double f = h - Math.Floor(h);
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        return sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }
}
