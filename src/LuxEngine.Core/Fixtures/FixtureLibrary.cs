using System.Text.Json;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Fixtures;

/// <summary>
/// All fixture models known to the app: bundled ones (read-only) plus fixtures the user created.
/// Library files contain either a single fixture or a JSON array of fixtures.
/// </summary>
public sealed class FixtureLibrary
{
    private readonly Dictionary<string, FixtureDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<FixtureDefinition> All => _definitions.Values;

    public IEnumerable<string> Manufacturers =>
        _definitions.Values.Select(d => d.Manufacturer).Distinct().OrderBy(m => m, StringComparer.OrdinalIgnoreCase);

    public FixtureDefinition? Get(string id) => _definitions.GetValueOrDefault(id);

    public void Add(FixtureDefinition definition) => _definitions[definition.Id] = definition;

    public bool Remove(string id) => _definitions.Remove(id);

    /// <summary>Loads every *.json file in a folder (recursively). Broken files are skipped and reported.</summary>
    public IReadOnlyList<string> LoadFolder(string folder, bool userDefined = false)
    {
        var errors = new List<string>();
        if (!Directory.Exists(folder)) return errors;

        foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                foreach (var def in ReadFile(file))
                {
                    def.IsUserDefined = userDefined;
                    Add(def);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return errors;
    }

    public static IReadOnlyList<FixtureDefinition> ReadFile(string file)
    {
        var json = File.ReadAllText(file);
        return json.TrimStart().StartsWith('[')
            ? JsonSerializer.Deserialize<List<FixtureDefinition>>(json, ShowSerializer.Options) ?? new()
            : new[] { JsonSerializer.Deserialize<FixtureDefinition>(json, ShowSerializer.Options)! };
    }

    /// <summary>Saves a user-defined fixture into the given folder as its own file.</summary>
    public static string SaveUserFixture(string folder, FixtureDefinition definition)
    {
        Directory.CreateDirectory(folder);
        var safe = string.Concat(definition.Id.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '_' : c));
        var path = Path.Combine(folder, safe + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(definition, ShowSerializer.Options));
        return path;
    }

    /// <summary>Case-insensitive search across manufacturer, model and category; every word must match.</summary>
    public IEnumerable<FixtureDefinition> Search(string? query, string? category = null)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _definitions.Values
            .Where(d => category is null || string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(d => words.All(w =>
                d.Manufacturer.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                d.Model.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                d.Category.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Model, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A plain fixture for users who just need N dimmer/RGB channels.</summary>
    public static FixtureDefinition CreateGeneric(string model, params ChannelType[] channels)
    {
        var def = new FixtureDefinition
        {
            Id = "generic/" + model.ToLowerInvariant().Replace(' ', '-'),
            Manufacturer = "Generic",
            Model = model,
            Category = channels.Any(c => c.IsColorMix()) ? "Color Changer" : "Dimmer",
        };
        var mode = new FixtureMode { Name = $"{channels.Length}-channel" };
        foreach (var type in channels)
        {
            var name = type.ToString();
            int n = 2;
            while (def.FindChannel(name) is not null) name = $"{type} {n++}";
            def.Channels.Add(new ChannelDefinition { Name = name, Type = type });
            mode.Channels.Add(name);
        }
        def.Modes.Add(mode);
        return def;
    }
}
