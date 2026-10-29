namespace LuxEngine.Core.Fixtures;

/// <summary>A light that has been patched into the show at a DMX address.</summary>
public sealed class FixtureInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string ModeName { get; set; } = "";

    /// <summary>LuxEngine universe number, starting at 1.</summary>
    public int Universe { get; set; } = 1;

    /// <summary>DMX start address, 1–512.</summary>
    public int Address { get; set; } = 1;

    /// <summary>Position on the stage view, 0–1 in both directions.</summary>
    /// <summary>Flips pan in the DMX output (for heads hung or facing the other way); controls keep working normally.</summary>
    public bool InvertPan { get; set; }

    /// <summary>Flips tilt in the DMX output.</summary>
    public bool InvertTilt { get; set; }

    public double StageX { get; set; } = 0.5;
    public double StageY { get; set; } = 0.5;
}

/// <summary>One channel of a patched fixture resolved to its absolute DMX slot.</summary>
public readonly record struct PatchedChannel(
    FixtureInstance Fixture,
    ChannelDefinition Channel,
    int Universe,
    int Address);

/// <summary>A fixture instance paired with its definition and mode.</summary>
public sealed class PatchedFixture
{
    public PatchedFixture(FixtureInstance instance, FixtureDefinition definition)
    {
        Instance = instance;
        Definition = definition;
        Mode = definition.FindMode(instance.ModeName) ?? new FixtureMode { Name = instance.ModeName };

        var channels = new List<PatchedChannel>();
        for (int i = 0; i < Mode.Channels.Count; i++)
        {
            var name = Mode.Channels[i];
            if (name is null) continue;
            var channel = definition.FindChannel(name);
            if (channel is null) continue;
            channels.Add(new PatchedChannel(instance, channel, instance.Universe, instance.Address + i));
        }
        Channels = channels;
        HasDimmer = channels.Any(c => c.Channel.Type.IsIntensity());
    }

    public FixtureInstance Instance { get; }
    public FixtureDefinition Definition { get; }
    public FixtureMode Mode { get; }
    public IReadOnlyList<PatchedChannel> Channels { get; }
    public bool HasDimmer { get; }

    public int LastAddress => Instance.Address + Mode.ChannelCount - 1;

    public bool Has(ChannelType type) => Channels.Any(c => c.Channel.Type == type);
}
