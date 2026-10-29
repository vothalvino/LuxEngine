using LuxEngine.Core.Fixtures;

namespace LuxEngine.Core.Engine;

/// <summary>Parameter control used by the keyboard: change one kind of channel on several lights at once.</summary>
public sealed partial class LightingEngine
{
    private readonly HashSet<Guid> _highlight = new();

    /// <summary>The value (0–255) of the first selected light that has a channel of this type, or null if none has it.</summary>
    public double? GetTypeValue(IEnumerable<Guid> fixtureIds, ChannelType type)
    {
        lock (_sync)
        {
            EnsurePatch();
            foreach (var id in fixtureIds)
                if (_fixtures.TryGetValue(id, out var f) && f.Channels.FirstOrDefault(c => c.Channel.Type == type) is { Channel: not null } ch)
                    return GetPlaybackValue(id, ch.Channel.Name);
            return null;
        }
    }

    public bool AnyHas(IEnumerable<Guid> fixtureIds, ChannelType type)
    {
        lock (_sync)
        {
            EnsurePatch();
            return fixtureIds.Any(id => _fixtures.TryGetValue(id, out var f) && f.Has(type));
        }
    }

    /// <summary>Moves every channel of this type on the lights by <paramref name="delta"/>, each from its own value, kept within 0–255.</summary>
    public void NudgeType(IEnumerable<Guid> fixtureIds, ChannelType type, int delta)
    {
        lock (_sync)
        {
            EnsurePatch();
            foreach (var id in fixtureIds)
                if (_fixtures.TryGetValue(id, out var f))
                    foreach (var ch in f.Channels.Where(c => c.Channel.Type == type))
                    {
                        double current = GetPlaybackValue(id, ch.Channel.Name) ?? ch.Channel.Default;
                        Programmer.Set(id, ch.Channel.Name, (byte)Math.Clamp(Math.Round(current) + delta, 0, 255));
                    }
        }
    }

    /// <summary>
    /// Moves to the next (+1) or previous (-1) named range, e.g. the next gobo, and sets each light to the middle
    /// of it. Returns the new range's name, or null when the channel has no named ranges.
    /// </summary>
    public string? StepRange(IEnumerable<Guid> fixtureIds, ChannelType type, int direction)
    {
        lock (_sync)
        {
            EnsurePatch();
            string? label = null;
            foreach (var id in fixtureIds)
            {
                if (!_fixtures.TryGetValue(id, out var f)) continue;
                foreach (var ch in f.Channels.Where(c => c.Channel.Type == type))
                {
                    var caps = ch.Channel.Capabilities.OrderBy(c => c.From).ToList();
                    if (caps.Count == 0) continue;
                    double current = GetPlaybackValue(id, ch.Channel.Name) ?? ch.Channel.Default;
                    int index = caps.FindIndex(c => current >= c.From && current <= c.To);
                    int next;
                    if (index >= 0) next = Math.Clamp(index + direction, 0, caps.Count - 1);   // stops at the first/last range
                    else
                    {
                        // Between ranges: go to the nearest one in the chosen direction.
                        next = direction > 0 ? caps.FindIndex(c => c.From > current) : caps.FindLastIndex(c => c.To < current);
                        if (next < 0) next = direction > 0 ? caps.Count - 1 : 0;
                    }
                    var cap = caps[next];
                    Programmer.Set(id, ch.Channel.Name, (byte)((cap.From + cap.To) / 2));
                    label ??= cap.Label;
                }
            }
            return label;
        }
    }

    /// <summary>The named range the first light is in for this channel type, e.g. "Gobo 3", or null.</summary>
    public string? RangeName(IEnumerable<Guid> fixtureIds, ChannelType type)
    {
        lock (_sync)
        {
            EnsurePatch();
            foreach (var id in fixtureIds)
                if (_fixtures.TryGetValue(id, out var f) && f.Channels.FirstOrDefault(c => c.Channel.Type == type) is { Channel: not null } ch)
                {
                    double v = GetPlaybackValue(id, ch.Channel.Name) ?? 0;
                    return ch.Channel.Capabilities.FirstOrDefault(c => v >= c.From && v <= c.To)?.Label;
                }
            return null;
        }
    }

    /// <summary>
    /// Warm/cold balance of white-only lights, 0 = all warm, 1 = all cold, or null when none has both.
    /// </summary>
    public double? GetColdness(IEnumerable<Guid> fixtureIds)
    {
        foreach (var id in fixtureIds)
        {
            var warm = GetTypeValue(new[] { id }, ChannelType.WarmWhite);
            var cold = GetTypeValue(new[] { id }, ChannelType.ColdWhite);
            if (warm is null || cold is null) continue;
            return warm + cold <= 0 ? 0.5 : cold / (warm + cold);
        }
        return null;
    }

    /// <summary>Shifts warm/cold white lights towards cold (1) or warm (0), keeping their brightness.</summary>
    public void SetColdness(IEnumerable<Guid> fixtureIds, double coldness)
    {
        coldness = Math.Clamp(coldness, 0, 1);
        lock (_sync)
        {
            EnsurePatch();
            foreach (var id in fixtureIds)
            {
                if (!_fixtures.TryGetValue(id, out var f) || !f.Has(ChannelType.WarmWhite) || !f.Has(ChannelType.ColdWhite)) continue;
                var warm = f.Channels.First(c => c.Channel.Type == ChannelType.WarmWhite).Channel.Name;
                var cold = f.Channels.First(c => c.Channel.Type == ChannelType.ColdWhite).Channel.Name;
                double level = Math.Max(GetPlaybackValue(id, warm) ?? 0, GetPlaybackValue(id, cold) ?? 0);
                if (level <= 0) level = 255;
                // The stronger side stays at the light's level, so brightness does not dip in the middle.
                double w = coldness <= 0.5 ? 1 : (1 - coldness) * 2;
                double c = coldness >= 0.5 ? 1 : coldness * 2;
                Programmer.Set(id, warm, (byte)Math.Round(level * w));
                Programmer.Set(id, cold, (byte)Math.Round(level * c));
            }
        }
    }

    // ---------------------------------------------------------------- Highlight

    /// <summary>Lights shown at full, open white with centred beams to find and focus them. Never part of presets.</summary>
    public IReadOnlyCollection<Guid> Highlighted
    {
        get { lock (_sync) return _highlight.ToList(); }
    }

    public void SetHighlight(IEnumerable<Guid> fixtureIds)
    {
        lock (_sync)
        {
            _highlight.Clear();
            _highlight.UnionWith(fixtureIds);
        }
    }

    public void ClearHighlight()
    {
        lock (_sync) _highlight.Clear();
    }

    /// <summary>Applied after effects, so a highlighted light is steady, bright and white.</summary>
    private void ApplyHighlight(Dictionary<ChannelKey, double> values, Dictionary<Guid, double> brightness)
    {
        foreach (var id in _highlight)
        {
            if (!_fixtures.TryGetValue(id, out var f)) continue;
            brightness[id] = 1;
            bool hasWhite = f.Has(ChannelType.White);
            foreach (var ch in f.Channels)
            {
                double? v = ch.Channel.Type switch
                {
                    ChannelType.Intensity => 255,
                    ChannelType.Red or ChannelType.Green or ChannelType.Blue => hasWhite ? 0 : 255,
                    ChannelType.White or ChannelType.WarmWhite or ChannelType.ColdWhite => 255,
                    ChannelType.Amber or ChannelType.Lime or ChannelType.UV or ChannelType.Indigo => 0,
                    ChannelType.Cyan or ChannelType.Magenta or ChannelType.Yellow => 0,
                    ChannelType.ColorWheel or ChannelType.Gobo or ChannelType.Prism or ChannelType.Frost => OpenValue(ch.Channel),
                    ChannelType.Pan or ChannelType.Tilt => 128,
                    ChannelType.Shutter or ChannelType.Strobe => OpenValue(ch.Channel, preferLabel: "open"),
                    _ => null,
                };
                if (v is double value) values[new ChannelKey(id, ch.Channel.Name)] = value;
            }
        }
    }

    /// <summary>The "open"/"no gobo"/"white" value of a channel: a range labelled like that, otherwise 0.</summary>
    private static double OpenValue(ChannelDefinition channel, string preferLabel = "open")
    {
        var cap = channel.Capabilities.FirstOrDefault(c => c.Label.Contains(preferLabel, StringComparison.OrdinalIgnoreCase))
                  ?? channel.Capabilities.FirstOrDefault(c => c.Label.Contains("white", StringComparison.OrdinalIgnoreCase) || c.Label.Contains("no ", StringComparison.OrdinalIgnoreCase));
        return cap is null ? 0 : (cap.From + cap.To) / 2.0;
    }
}
