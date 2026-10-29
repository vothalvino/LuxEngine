namespace LuxEngine.Core.Fixtures;

/// <summary>A channel as edited by the user, remembering which existing channel it came from.</summary>
public sealed record EditedChannel(string? OriginalName, ChannelDefinition Channel);

public static class FixtureTypeEditing
{
    /// <summary>
    /// Builds a new version of <paramref name="original"/> where one mode has the given channels.
    /// Other modes and the channels they use stay as they were. Also returns how each channel of the
    /// edited mode was renamed (old name → new name, or null when it was removed).
    /// </summary>
    public static (FixtureDefinition Definition, Dictionary<string, string?> Renames) EditMode(
        FixtureDefinition original, string modeName, string newModeName, IReadOnlyList<EditedChannel> channels,
        string manufacturer, string model, string category)
    {
        var oldMode = original.Modes.FirstOrDefault(m => m.Name == modeName);
        var otherModes = original.Modes.Where(m => m != oldMode).ToList();
        var usedElsewhere = otherModes.SelectMany(m => m.Channels).OfType<string>().ToHashSet();

        var definition = new FixtureDefinition
        {
            Id = original.Id,
            Manufacturer = manufacturer,
            Model = model,
            Category = category,
            IsUserDefined = original.IsUserDefined,
        };
        var byName = new Dictionary<string, ChannelDefinition>();
        foreach (var ch in original.Channels.Where(c => usedElsewhere.Contains(c.Name)))
            byName[ch.Name] = ch;
        foreach (var edited in channels)
            byName[edited.Channel.Name] = edited.Channel;

        // Keep the original channel order where possible, then any new channels.
        definition.Channels.AddRange(original.Channels.Where(c => byName.ContainsKey(c.Name)).Select(c => byName[c.Name]));
        definition.Channels.AddRange(channels.Select(c => c.Channel).Where(c => !original.Channels.Any(o => o.Name == c.Name)));

        var mode = new FixtureMode { Name = newModeName, Channels = channels.Select(c => (string?)c.Channel.Name).ToList() };
        foreach (var m in original.Modes)
            definition.Modes.Add(m == oldMode ? mode : m);
        if (oldMode is null) definition.Modes.Add(mode);

        var renames = new Dictionary<string, string?>();
        foreach (var name in oldMode?.Channels.OfType<string>() ?? Enumerable.Empty<string>())
            renames[name] = channels.FirstOrDefault(c => c.OriginalName == name)?.Channel.Name;
        return (definition, renames);
    }
}
