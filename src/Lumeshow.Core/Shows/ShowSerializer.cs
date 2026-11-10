using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumeshow.Core.Shows;

/// <summary>Reads and writes .lumeshow files (indented JSON).</summary>
public static class ShowSerializer
{
    public const string FileExtension = ".lumeshow";

    /// <summary>What show files were called while the app was named LuxEngine. They still open; the content is the same.</summary>
    public const string LegacyFileExtension = ".luxshow";

    /// <summary>True for a path that ends in the current or the old show file extension.</summary>
    public static bool IsShowFile(string? path) =>
        path is not null && (path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(LegacyFileExtension, StringComparison.OrdinalIgnoreCase));

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

    private static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };

    public static string Serialize(Show show) => JsonSerializer.Serialize(show, Options);

    /// <summary>The show on one line: smaller and quicker, used for undo snapshots.</summary>
    public static string SerializeCompact(Show show) => JsonSerializer.Serialize(show, CompactOptions);

    public static Show Deserialize(string json)
    {
        var show = JsonSerializer.Deserialize<Show>(json, Options)
            ?? throw new InvalidDataException("The show file is empty.");
        if (show.Version > Show.CurrentVersion)
            throw new InvalidDataException("This show was made with a newer version of Lumeshow. Please update the app.");
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
