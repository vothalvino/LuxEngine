namespace LuxEngine.Core.Fixtures;

/// <summary>What a single DMX channel of a fixture controls.</summary>
public enum ChannelType
{
    Generic,
    Intensity,
    Red,
    Green,
    Blue,
    White,
    WarmWhite,
    ColdWhite,
    Amber,
    Lime,
    Indigo,
    UV,
    Cyan,
    Magenta,
    Yellow,
    ColorTemperature,
    ColorWheel,
    Pan,
    PanFine,
    Tilt,
    TiltFine,
    Gobo,
    GoboRotation,
    Prism,
    Focus,
    Zoom,
    Iris,
    Frost,
    Shutter,
    Strobe,
    Speed,
    Effect,
    Maintenance,
}

public static class ChannelTypeExtensions
{
    public static bool IsIntensity(this ChannelType type) => type == ChannelType.Intensity;

    /// <summary>Additive or subtractive color mixing channels.</summary>
    public static bool IsColorMix(this ChannelType type) => type is
        ChannelType.Red or ChannelType.Green or ChannelType.Blue or ChannelType.White or
        ChannelType.WarmWhite or ChannelType.ColdWhite or ChannelType.Amber or ChannelType.Lime or
        ChannelType.Indigo or ChannelType.UV or ChannelType.Cyan or ChannelType.Magenta or ChannelType.Yellow;

    /// <summary>Additive emitters whose output scales with brightness (used when a fixture has no dimmer).</summary>
    public static bool IsAdditiveColor(this ChannelType type) => type.IsColorMix() &&
        type is not (ChannelType.Cyan or ChannelType.Magenta or ChannelType.Yellow);

    /// <summary>Channels that jump to their new value instead of fading (wheels, modes, macros).</summary>
    public static bool Snaps(this ChannelType type) => type is
        ChannelType.ColorWheel or ChannelType.Gobo or ChannelType.GoboRotation or ChannelType.Prism or
        ChannelType.Shutter or ChannelType.Strobe or ChannelType.Effect or ChannelType.Maintenance or
        ChannelType.Speed;

    public static string Group(this ChannelType type) => type switch
    {
        ChannelType.Intensity => "Dimmer",
        _ when type.IsColorMix() || type is ChannelType.ColorWheel or ChannelType.ColorTemperature => "Color",
        ChannelType.Pan or ChannelType.PanFine or ChannelType.Tilt or ChannelType.TiltFine => "Position",
        ChannelType.Gobo or ChannelType.GoboRotation or ChannelType.Prism or ChannelType.Focus or
            ChannelType.Zoom or ChannelType.Iris or ChannelType.Frost => "Beam",
        ChannelType.Shutter or ChannelType.Strobe => "Shutter",
        _ => "Other",
    };
}
