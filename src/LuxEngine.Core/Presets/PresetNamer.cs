using System.Text.RegularExpressions;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Presets;

/// <summary>
/// Makes a readable name from what a preset contains, e.g. "Pars · Blue · Full" or "Spot 1–2 · Center".
/// </summary>
public static class PresetNamer
{
    public static string Suggest(Show show, Preset preset)
    {
        var fixtures = show.Fixtures.Where(f => preset.FixtureIds.Contains(f.Id)).ToList();
        if (fixtures.Count == 0) return "Empty preset";

        var parts = new List<string> { Lights(show, fixtures) };
        var byFixture = preset.Values.GroupBy(v => v.FixtureId).ToDictionary(g => g.Key, g => g.ToList());
        var types = new Dictionary<(Guid, string), ChannelType>();
        foreach (var f in fixtures)
            foreach (var ch in show.DefinitionOf(f)?.Channels ?? new())
                types[(f.Id, ch.Name)] = ch.Type;
        ChannelType TypeOf(PresetValue v) => types.GetValueOrDefault((v.FixtureId, v.Channel), ChannelType.Generic);

        if (ColorName(fixtures.Select(f => byFixture.GetValueOrDefault(f.Id)).OfType<List<PresetValue>>(), TypeOf) is { } color)
            parts.Add(color);

        var dimmers = preset.Values.Where(v => TypeOf(v) == ChannelType.Intensity).Select(v => (int)v.Value).Distinct().ToList();
        if (dimmers.Count == 1) parts.Add(dimmers[0] switch { 0 => "Off", 255 => "Full", var d => $"{Math.Round(d / 2.55)}%" });
        else if (dimmers.Count > 1) parts.Add("Dimmer");

        var pan = preset.Values.Where(v => TypeOf(v) == ChannelType.Pan).Select(v => (int)v.Value).ToList();
        var tilt = preset.Values.Where(v => TypeOf(v) == ChannelType.Tilt).Select(v => (int)v.Value).ToList();
        if (pan.Count + tilt.Count > 0)
            parts.Add(pan.Concat(tilt).All(v => Math.Abs(v - 128) <= 8) ? "Center" : "Position");

        foreach (var name in OtherSettings(preset, TypeOf, show)) parts.Add(name);
        foreach (var type in preset.Effects.Select(e => e.Type).Distinct()) parts.Add(type.DisplayName());

        // The light names plus at most three details keep the name short enough for the preset list.
        return string.Join(" · ", parts.Take(4));
    }

    /// <summary>"Pars", "Par 1–4", "Par 1, 3", "Pars + Spots", "All lights" or "5 lights".</summary>
    internal static string Lights(Show show, List<FixtureInstance> fixtures)
    {
        if (fixtures.Count == show.Fixtures.Count && fixtures.Count > 1) return "All lights";
        var parsed = fixtures.Select(f =>
        {
            var m = Regex.Match(f.Name.Trim(), @"^(.*?)\s*(\d+)$");
            return m.Success ? (Base: m.Groups[1].Value, Number: (int?)int.Parse(m.Groups[2].Value)) : (Base: f.Name.Trim(), Number: (int?)null);
        }).ToList();
        var bases = parsed.Select(p => p.Base).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (fixtures.Count == 1) return fixtures[0].Name;

        if (bases.Count == 1)
        {
            var name = bases[0];
            int inShow = show.Fixtures.Count(f => Regex.Replace(f.Name.Trim(), @"\s*\d+$", "").Equals(name, StringComparison.OrdinalIgnoreCase));
            if (inShow == fixtures.Count) return Plural(name);
            var numbers = parsed.Select(p => p.Number).OfType<int>().Order().ToList();
            if (numbers.Count == fixtures.Count)
            {
                bool consecutive = numbers.Zip(numbers.Skip(1)).All(p => p.Second == p.First + 1);
                if (consecutive) return $"{name} {numbers[0]}–{numbers[^1]}";
                if (numbers.Count <= 3) return $"{name} {string.Join(", ", numbers)}";
            }
            return $"{fixtures.Count} {Plural(name)}";
        }
        return bases.Count == 2 ? $"{Plural(bases[0])} + {Plural(bases[1])}" : $"{fixtures.Count} lights";
    }

    private static string Plural(string name) =>
        name.EndsWith("s", StringComparison.OrdinalIgnoreCase) || name.Length == 0 ? name : name + "s";

    /// <summary>The color most lights in the preset show, or "Red + Blue" for two, or "Mixed colors".</summary>
    private static string? ColorName(IEnumerable<List<PresetValue>> perFixture, Func<PresetValue, ChannelType> typeOf)
    {
        var names = new List<string>();
        foreach (var values in perFixture)
        {
            double Get(ChannelType t) => values.Where(v => typeOf(v) == t).Select(v => v.Value / 255.0).DefaultIfEmpty(-1).Max();
            double red = Get(ChannelType.Red), green = Get(ChannelType.Green), blue = Get(ChannelType.Blue);
            double white = Get(ChannelType.White), warm = Get(ChannelType.WarmWhite), cold = Get(ChannelType.ColdWhite);
            double amber = Get(ChannelType.Amber), uv = Get(ChannelType.UV);
            double c = Get(ChannelType.Cyan), m = Get(ChannelType.Magenta), y = Get(ChannelType.Yellow);
            bool hasRgb = red >= 0 || green >= 0 || blue >= 0;
            bool hasCmy = c >= 0 || m >= 0 || y >= 0;
            static double Z(double v) => Math.Max(0, v);

            string? name = null;
            if (hasRgb || amber >= 0 || uv >= 0)
            {
                double r = Z(red) + Z(amber) + Z(uv) * 0.4, g = Z(green) + Z(amber) * 0.6, b = Z(blue) + Z(uv);
                double whites = Math.Max(Z(white), Math.Max(Z(warm), Z(cold)));
                if (whites > Math.Max(r, Math.Max(g, b)) * 0.8 && whites > 0) name = WhiteName(Z(warm), Z(cold));
                else name = Hue(r, g, b);
            }
            else if (hasCmy) name = Hue(1 - Z(c), 1 - Z(m), 1 - Z(y));
            else if (white >= 0 || warm >= 0 || cold >= 0)
                name = Math.Max(Z(white), Math.Max(Z(warm), Z(cold))) > 0 ? WhiteName(Z(warm), Z(cold)) : null;
            if (name is not null) names.Add(name);
        }

        var distinct = names.GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        return distinct.Count switch
        {
            0 => null,
            1 => distinct[0],
            2 => $"{distinct[0]} + {distinct[1]}",
            _ => "Mixed colors",
        };
    }

    private static string WhiteName(double warm, double cold) =>
        warm > cold * 1.5 ? "Warm white" : cold > warm * 1.5 ? "Cold white" : "White";

    /// <summary>Names an RGB color (0–1 each) by its hue; very pale colors are "White", black gives null.</summary>
    internal static string? Hue(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max <= 0.02) return null;
        double saturation = (max - min) / max;
        if (saturation < 0.2) return "White";
        double h;
        if (max == r) h = 60 * (((g - b) / (max - min)) % 6);
        else if (max == g) h = 60 * ((b - r) / (max - min) + 2);
        else h = 60 * ((r - g) / (max - min) + 4);
        if (h < 0) h += 360;

        string name = h switch
        {
            < 15 => "Red",
            < 40 => "Orange",
            < 70 => "Yellow",
            < 160 => "Green",
            < 195 => "Cyan",
            < 255 => "Blue",
            < 290 => "Purple",
            < 335 => "Magenta",
            _ => "Red",
        };
        return name is "Red" or "Magenta" && saturation < 0.6 ? "Pink" : name;
    }

    /// <summary>Gobo, strobe and similar settings, named by their range label when the fixture has one.</summary>
    private static IEnumerable<string> OtherSettings(Preset preset, Func<PresetValue, ChannelType> typeOf, Show show)
    {
        var shown = new HashSet<string>();
        foreach (var v in preset.Values)
        {
            var type = typeOf(v);
            if (type is not (ChannelType.Gobo or ChannelType.ColorWheel or ChannelType.Strobe or ChannelType.Shutter or ChannelType.Prism or ChannelType.Effect))
                continue;
            var fixture = show.FindFixture(v.FixtureId);
            var channel = fixture is null ? null : show.DefinitionOf(fixture)?.Channels.FirstOrDefault(c => c.Name == v.Channel);
            var label = channel?.Capabilities.FirstOrDefault(c => v.Value >= c.From && v.Value <= c.To)?.Label;
            if (type is ChannelType.Strobe or ChannelType.Shutter && label is null) label = v.Value == 0 ? null : "Strobe";
            if (label is not null && shown.Add(label)) yield return label;
        }
    }
}
