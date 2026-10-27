namespace LuxEngine.Core.Fixtures;

/// <summary>A named DMX value range on a channel, e.g. "Strobe slow → fast" on 10–255.</summary>
public sealed class ChannelCapability
{
    public int From { get; set; }
    public int To { get; set; } = 255;
    public string Label { get; set; } = "";
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
