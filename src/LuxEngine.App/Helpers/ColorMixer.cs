using LuxEngine.Core.Engine;
using LuxEngine.Core.Fixtures;
using Windows.UI;

namespace LuxEngine.App.Helpers;

/// <summary>Turns a picked color into the right channel values for each kind of fixture.</summary>
public static class ColorMixer
{
    public static void Apply(LightingEngine engine, PatchedFixture fixture, Color color, double brightness = 1)
    {
        double r = color.R * brightness, g = color.G * brightness, b = color.B * brightness;
        bool hasRgb = fixture.Has(ChannelType.Red) || fixture.Has(ChannelType.Green) || fixture.Has(ChannelType.Blue);

        // Pure white uses the white LED when the fixture has one; colors leave it off.
        bool isWhite = color.R == color.G && color.G == color.B && color.R > 0;
        double white = isWhite ? r : 0;
        bool useWhiteLed = isWhite && fixture.Has(ChannelType.White);

        foreach (var ch in fixture.Channels)
        {
            double? value = ch.Channel.Type switch
            {
                ChannelType.Red => useWhiteLed ? 0 : r,
                ChannelType.Green => useWhiteLed ? 0 : g,
                ChannelType.Blue => useWhiteLed ? 0 : b,
                ChannelType.White => white,
                ChannelType.WarmWhite or ChannelType.ColdWhite or ChannelType.Amber or ChannelType.Lime
                    or ChannelType.UV or ChannelType.Indigo when hasRgb => 0,
                ChannelType.Cyan => 255 - color.R,
                ChannelType.Magenta => 255 - color.G,
                ChannelType.Yellow => 255 - color.B,
                _ => null,
            };
            if (value is double v)
                engine.SetValue(fixture.Instance.Id, ch.Channel.Name, (byte)Math.Clamp(Math.Round(v), 0, 255));
        }
    }

    public static Color FromLook(FixtureLook look, double minimum = 0)
    {
        double i = Math.Max(minimum, look.Intensity);
        return Color.FromArgb(255, (byte)(look.Red * 255 * i), (byte)(look.Green * 255 * i), (byte)(look.Blue * 255 * i));
    }

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return fallback;
        try
        {
            return Color.FromArgb(255,
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
