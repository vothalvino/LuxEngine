using Lumeshow.Core.Migration;
using Lumeshow.Core.Shows;
using Lumeshow.Core.Updates;

namespace Lumeshow.Core.Tests;

/// <summary>What has to keep working for people who used the app while it was called LuxEngine.</summary>
public class RenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lume-rename-" + Guid.NewGuid());

    public RenameTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name) => Path.Combine(_root, name);

    private string OldFolderWithFiles()
    {
        var old = Folder(LegacyFolders.OldName);
        Directory.CreateDirectory(Path.Combine(old, "Sub"));
        File.WriteAllText(Path.Combine(old, "settings.json"), "{\"theme\":\"Dark\"}");
        File.WriteAllText(Path.Combine(old, "Sub", "mine.json"), "[]");
        return old;
    }

    [Fact]
    public void The_old_folder_moves_to_the_new_name()
    {
        var old = OldFolderWithFiles();
        var now = Folder("Lumeshow");

        Assert.True(LegacyFolders.Move(old, now));

        Assert.False(Directory.Exists(old));
        Assert.Equal("{\"theme\":\"Dark\"}", File.ReadAllText(Path.Combine(now, "settings.json")));
        Assert.True(File.Exists(Path.Combine(now, "Sub", "mine.json")));
    }

    [Fact]
    public void An_empty_new_folder_does_not_stop_the_move()
    {
        var old = OldFolderWithFiles();
        var now = Folder("Lumeshow");
        Directory.CreateDirectory(now);

        Assert.True(LegacyFolders.Move(old, now));
        Assert.True(File.Exists(Path.Combine(now, "settings.json")));
    }

    [Fact]
    public void A_new_folder_that_is_in_use_is_never_overwritten()
    {
        var old = OldFolderWithFiles();
        var now = Folder("Lumeshow");
        Directory.CreateDirectory(now);
        File.WriteAllText(Path.Combine(now, "settings.json"), "{\"theme\":\"Light\"}");

        Assert.False(LegacyFolders.Move(old, now));

        Assert.Equal("{\"theme\":\"Light\"}", File.ReadAllText(Path.Combine(now, "settings.json")));
        Assert.True(File.Exists(Path.Combine(old, "settings.json")));
    }

    [Fact]
    public void Nothing_happens_without_an_old_folder()
    {
        Assert.False(LegacyFolders.Move(Folder(LegacyFolders.OldName), Folder("Lumeshow")));
        Assert.False(Directory.Exists(Folder("Lumeshow")));
    }

    [Fact]
    public void The_new_folder_may_be_deeper_than_the_old_one()
    {
        // Documents\LuxEngine\Fixtures -> Documents\Lumeshow\Fixtures: the new parent does not exist yet.
        var oldParent = Folder(LegacyFolders.OldName);
        var old = Path.Combine(oldParent, "Fixtures");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "my-par.json"), "[]");
        File.WriteAllText(Path.Combine(oldParent, "My show.luxshow"), "{}");
        var now = Path.Combine(Folder("Lumeshow"), "Fixtures");

        Assert.True(LegacyFolders.Move(old, now));
        LegacyFolders.RemoveIfEmpty(oldParent);

        Assert.True(File.Exists(Path.Combine(now, "my-par.json")));
        // The user's own show is still where they saved it, so the old folder stays.
        Assert.True(File.Exists(Path.Combine(oldParent, "My show.luxshow")));
    }

    [Fact]
    public void An_old_folder_with_nothing_left_is_removed()
    {
        var oldParent = Folder(LegacyFolders.OldName);
        var old = Path.Combine(oldParent, "Fixtures");
        Directory.CreateDirectory(old);

        Assert.True(LegacyFolders.Move(old, Path.Combine(Folder("Lumeshow"), "Fixtures")));
        LegacyFolders.RemoveIfEmpty(oldParent);

        Assert.False(Directory.Exists(oldParent));
    }

    [Fact]
    public void Old_and_new_show_files_are_both_show_files()
    {
        Assert.Equal(".lumeshow", ShowSerializer.FileExtension);
        Assert.True(ShowSerializer.IsShowFile(@"C:\Shows\Friday.lumeshow"));
        Assert.True(ShowSerializer.IsShowFile(@"C:\Shows\Friday.luxshow"));
        Assert.True(ShowSerializer.IsShowFile(@"C:\Shows\FRIDAY.LUXSHOW"));
        Assert.False(ShowSerializer.IsShowFile(@"C:\Shows\Friday.json"));
        Assert.False(ShowSerializer.IsShowFile(null));
    }

    [Fact]
    public void A_show_saved_by_the_old_name_opens_unchanged()
    {
        var t = new TestShow();
        t.AddCue(2, t.AddPreset("Look", (t.Par1, "Intensity", 200)));
        var path = Path.Combine(_root, "Old" + ShowSerializer.LegacyFileExtension);
        ShowSerializer.Save(t.Show, path);

        var loaded = ShowSerializer.Load(path);

        Assert.Equal(3, loaded.Fixtures.Count);
        Assert.Single(loaded.CueList.Cues);
    }

    [Fact]
    public void Updates_are_looked_up_under_the_new_name()
    {
        Assert.Contains("/vothalvino/Lumeshow/", UpdateCheck.LatestReleaseApi);
        var json = """{"tag_name":"v9.0.0","html_url":"https://github.com/vothalvino/Lumeshow/releases/tag/v9.0.0"}""";
        Assert.Equal("https://github.com/vothalvino/Lumeshow/releases/tag/v9.0.0", UpdateCheck.FromJson(json, new Version(2, 0, 0))!.Url);
        // A page anywhere else is never opened, also not under the old name: the releases page is used instead.
        var elsewhere = """{"tag_name":"v9.0.0","html_url":"https://github.com/someone/LuxEngine/releases/tag/v9.0.0"}""";
        Assert.Equal(UpdateCheck.ReleasesPage, UpdateCheck.FromJson(elsewhere, new Version(2, 0, 0))!.Url);
    }
}
