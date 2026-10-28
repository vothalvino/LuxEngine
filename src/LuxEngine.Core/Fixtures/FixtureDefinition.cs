namespace LuxEngine.Core.Fixtures;

/// <summary>A named DMX value range on a channel, e.g. "Strobe slow → fast" on 10–255.</summary>
public sealed class ChannelCapability
{
    public int From { get; set; }
    public int To { get; set; } = 255;
    public string Label { get; set; } = "";

    /// <summary>Returns a user-facing problem with a channel's ranges, or null when they are fine.</summary>
    public static string? Validate(IReadOnlyList<ChannelCapability> capabilities)
    {
        foreach (var cap in capabilities)
        {
            if (cap.From is < 0 or > 255 || cap.To is < 0 or > 255) return "Range values must be between 0 and 255.";
            if (cap.From > cap.To) return $"The range {cap.From}–{cap.To} starts after it ends.";
            if (string.IsNullOrWhiteSpace(cap.Label)) return $"The range {cap.From}–{cap.To} needs a description.";
        }
        var sorted = capabilities.OrderBy(c => c.From).ToList();
        for (int i = 1; i < sorted.Count; i++)
            if (sorted[i].From <= sorted[i - 1].To)
                return $"The ranges \"{sorted[i - 1].Label}\" and \"{sorted[i].Label}\" overlap.";
        return null;
    }
}

public sealed class ChannelDefinition
{
    public string Name { get; set; } = "";
    public ChannelType Type { get; set; } = ChannelType.Generic;
    public byte Default { get; set; }
    public List<ChannelCapability> Capabilities { get; set; } = new();
}

/// <summary>One DMX personality of a fixture: the ordered list of channels it occupies.</summary>
public sealed class FixtureMode
{
    public string Name { get; set; } = "";

    /// <summary>Channel names in DMX order. A null entry is an unused slot.</summary>
    public List<string?> Channels { get; set; } = new();

    public int ChannelCount => Channels.Count;
}

/// <summary>A light model, e.g. "Chauvet SlimPAR 56".</summary>
public sealed class FixtureDefinition
{
    /// <summary>Unique key, e.g. "chauvet-dj/slimpar-56".</summary>
    public string Id { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string Category { get; set; } = "Other";

    /// <summary>True for fixtures the user created themselves.</summary>
    public bool IsUserDefined { get; set; }

    public List<ChannelDefinition> Channels { get; set; } = new();
    public List<FixtureMode> Modes { get; set; } = new();

    public string DisplayName => string.IsNullOrEmpty(Manufacturer) ? Model : $"{Manufacturer} {Model}";

    public ChannelDefinition? FindChannel(string name) =>
        Channels.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public FixtureMode? FindMode(string? name) =>
        Modes.FirstOrDefault(m => m.Name == name) ?? Modes.FirstOrDefault();

    public override string ToString() => DisplayName;
}
