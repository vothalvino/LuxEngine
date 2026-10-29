namespace LuxEngine.Core.Fixtures;

/// <summary>How fast a light flashes for a strobe/shutter value, used to make the stage view flicker like the real light.</summary>
public readonly record struct StrobeRate(double FlashesPerSecond, bool Random);

public static class Strobe
{
    public const double SlowestHz = 1, FastestHz = 20;

    /// <summary>
    /// The flash rate for a value of a strobe or shutter channel, or null when the light is not strobing
    /// (open, closed, off). Named ranges decide: "Strobe slow → fast" runs from slow at the start of the range
    /// to fast at its end ("fast → slow" the other way), "Random" flashes irregularly. A strobe channel without
    /// named ranges is off near 0 and gets faster towards 255.
    /// </summary>
    public static StrobeRate? Rate(ChannelDefinition channel, double value)
    {
        if (channel.Capabilities.Count == 0)
        {
            if (channel.Type != ChannelType.Strobe || value < 8) return null;
            return new StrobeRate(Hz((value - 8) / (255 - 8)), false);
        }

        var cap = channel.Capabilities.FirstOrDefault(c => value >= c.From && value <= c.To);
        if (cap is null) return null;
        var label = cap.Label.ToLowerInvariant();
        bool strobing = label.Contains("strobe") || label.Contains("flash") || label.Contains("pulse") || label.Contains("random") || label.Contains("lightning");
        bool notStrobing = label.Contains("no strobe") || label.Contains("strobe off") || label.Contains("open") || label.Contains("closed");
        if (!strobing || notStrobing) return null;

        double t = cap.To > cap.From ? (value - cap.From) / (cap.To - cap.From) : 0.5;
        int slow = label.IndexOf("slow", StringComparison.Ordinal), fast = label.IndexOf("fast", StringComparison.Ordinal);
        if (slow >= 0 && fast >= 0 && fast < slow) t = 1 - t;   // "fast → slow"
        return new StrobeRate(Hz(t), label.Contains("random"));
    }

    /// <summary>Brightness factor (0 or 1) of a strobing light at a moment: short flashes, about a third of each cycle.</summary>
    public static double Flash(StrobeRate rate, double seconds, Guid fixtureId)
    {
        double cycles = seconds * rate.FlashesPerSecond;
        if (!rate.Random) return cycles - Math.Floor(cycles) < 0.35 ? 1 : 0;
        // Random: each cycle flashes or not, decided by a hash, so lights do not flash together.
        long slot = (long)Math.Floor(cycles);
        int hash = HashCode.Combine(slot, fixtureId);
        return (hash & 3) == 0 && cycles - slot < 0.5 ? 1 : 0;
    }

    private static double Hz(double t) => SlowestHz + Math.Clamp(t, 0, 1) * (FastestHz - SlowestHz);
}
