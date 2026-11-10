namespace Lumeshow.Core.Migration;

/// <summary>
/// Until version 1.8 the app was called LuxEngine and kept its files in folders of that name.
/// This moves such a folder to its new place the first time Lumeshow starts.
/// </summary>
public static class LegacyFolders
{
    /// <summary>The folder name the app used before it was renamed.</summary>
    public const string OldName = "LuxEngine";

    /// <summary>
    /// Moves <paramref name="oldFolder"/> to <paramref name="newFolder"/>, unless the new one already has
    /// something in it. When moving is not possible (a file is in use, another drive) the files are copied
    /// instead and the old folder is left alone. Returns true when the new folder now has the old files.
    /// Never throws: without the old files the app simply starts fresh.
    /// </summary>
    public static bool Move(string oldFolder, string newFolder)
    {
        try
        {
            if (!Directory.Exists(oldFolder)) return false;
            if (Directory.Exists(newFolder))
            {
                if (Directory.EnumerateFileSystemEntries(newFolder).Any()) return false;
                Directory.Delete(newFolder);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(newFolder))!);
            try
            {
                Directory.Move(oldFolder, newFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Copy(oldFolder, newFolder);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Removes a folder that has nothing left in it. Folders with files are never touched.</summary>
    public static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind does no harm.
        }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var folder in Directory.EnumerateDirectories(from))
            Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
}
