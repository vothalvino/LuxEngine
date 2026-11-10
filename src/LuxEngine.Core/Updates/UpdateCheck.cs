using System.Text.Json;

namespace LuxEngine.Core.Updates;

/// <summary>A newer LuxEngine release found on GitHub.</summary>
public sealed record UpdateInfo(string Version, string Url);

/// <summary>Reads GitHub's "latest release" answer and compares it with the running version.</summary>
public static class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/vothalvino/LuxEngine/releases/latest";
    public const string ReleasesPage = "https://github.com/vothalvino/LuxEngine/releases/latest";

    /// <summary>Turns "v1.6.0" or "1.6" into a version; null for anything else (e.g. preview tags).</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var text = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    public static bool IsNewer(Version current, string? tag) =>
        ParseTag(tag) is { } latest && latest > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));

    /// <summary>
    /// The update described by GitHub's JSON, or null when it is not newer than <paramref name="current"/>,
    /// is a draft or pre-release, or cannot be read.
    /// </summary>
    public static UpdateInfo? FromJson(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (!IsNewer(current, tag)) return null;
            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            // Only ever open a page on the project's own GitHub site.
            if (url is null || !url.StartsWith("https://github.com/vothalvino/LuxEngine/", StringComparison.OrdinalIgnoreCase)) url = ReleasesPage;
            return new UpdateInfo(ParseTag(tag)!.ToString(3), url);
        }
        catch (JsonException)
        {
            return null;
        }

        static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }
}
