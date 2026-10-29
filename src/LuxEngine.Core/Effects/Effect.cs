namespace LuxEngine.Core.Effects;

public enum EffectType
{
    /// <summary>Cycles colors through the rainbow.</summary>
    Rainbow,
    /// <summary>Turns lights on one after another.</summary>
    Chase,
    /// <summary>Smooth brightness wave rolling across the lights.</summary>
    Wave,
    /// <summary>Fast on/off flashing.</summary>
    Strobe,
    /// <summary>Moving heads draw a circle.</summary>
    Circle,
    /// <summary>Moving heads draw a figure 8.</summary>
    Figure8,
    /// <summary>Moving heads swing left and right.</summary>
    PanSweep,
    /// <summary>Moving heads swing up and down.</summary>
    TiltSweep,
}

public static class EffectTypes
{
    /// <summary>Effects that move pan/tilt (only one of them runs on a light at a time).</summary>
    public static bool IsMovement(this EffectType type) =>
        type is EffectType.Circle or EffectType.Figure8 or EffectType.PanSweep or EffectType.TiltSweep;

    /// <summary>Name shown to people, e.g. "Figure 8".</summary>
    public static string DisplayName(this EffectType type) => type switch
    {
        EffectType.Figure8 => "Figure 8",
        EffectType.PanSweep => "Pan sweep",
        EffectType.TiltSweep => "Tilt sweep",
        _ => type.ToString(),
    };
}

/// <summary>A running effect applied to a set of fixtures.</summary>
public sealed class Effect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EffectType Type { get; set; }

    /// <summary>Cycles per second.</summary>
    public double Speed { get; set; } = 0.5;

    /// <summary>Strength of the effect, 0–1 (depth for dimmer effects, radius for movement).</summary>
    public double Size { get; set; } = 1.0;

    /// <summary>How much the lights are out of step with each other, 0 (all together) to 1 (spread evenly).</summary>
    public double Spread { get; set; } = 1.0;

    public List<Guid> FixtureIds { get; set; } = new();

    public Effect Clone() => new()
    {
        Id = Guid.NewGuid(), Type = Type, Speed = Speed, Size = Size, Spread = Spread,
        FixtureIds = new List<Guid>(FixtureIds),
    };
}
