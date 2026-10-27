using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuxEngine.Core.Shows;

/// <summary>Reads and writes .luxshow files (indented JSON).</summary>
public static class ShowSerializer
{
    public const string FileExtension = ".luxshow";

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(Show show) => JsonSerializer.Serialize(show, Options);

    public static Show Deserialize(string json)
    {
        var show = JsonSerializer.Deserialize<Show>(json, Options)
            ?? throw new InvalidDataException("The show file is empty.");
        if (show.Version > Show.CurrentVersion)
            throw new InvalidDataException("This show was made with a newer version of LuxEngine. Please update the app.");
        return show;
    }

    /// <summary>Writes to a temporary file first, so a crash during saving never corrupts the show.</summary>
    public static void Save(Show show, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(show));
        File.Move(temp, path, overwrite: true);
    }

    public static Show Load(string path) => Deserialize(File.ReadAllText(path));
}
