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
    /// <summary>Moving heads trace a shape from the show's shape library (see <see cref="Effect.ShapeId"/>).</summary>
    Shape,
}

public static class EffectTypes
{
    /// <summary>Effects that move pan/tilt (only one of them runs on a light at a time).</summary>
    public static bool IsMovement(this EffectType type) =>
        type is EffectType.Circle or EffectType.Figure8 or EffectType.PanSweep or EffectType.TiltSweep or EffectType.Shape;

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

    /// <summary>Movement: the shape to trace when <see cref="Type"/> is <see cref="EffectType.Shape"/>.</summary>
    public Guid? ShapeId { get; set; }

    /// <summary>Movement: keeps each head inside its box of this area. Null moves around the current aim.</summary>
    public Guid? AreaId { get; set; }

    /// <summary>Movement: how wide (pan) and how tall (tilt) the figure is, 0–1, on top of <see cref="Size"/>.</summary>
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;

    /// <summary>Runs the figure the other way round.</summary>
    public bool Reverse { get; set; }

    /// <summary>An exact copy, keeping the same id (for reading the programmer safely).</summary>
    public Effect Copy()
    {
        var copy = Clone();
        copy.Id = Id;
        return copy;
    }

    public Effect Clone() => new()
    {
        Id = Guid.NewGuid(), Type = Type, Speed = Speed, Size = Size, Spread = Spread,
        FixtureIds = new List<Guid>(FixtureIds),
        ShapeId = ShapeId, AreaId = AreaId, Width = Width, Height = Height, Reverse = Reverse,
    };
}
