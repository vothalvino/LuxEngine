using Lumeshow.Core.Engine;
using Lumeshow.Core.Fixtures;
using Lumeshow.Core.Shows;
using Lumeshow.Core.Updates;

namespace Lumeshow.Core.Tests;

public class UndoAndGroupTests
{
    [Fact]
    public void Undo_and_redo_walk_through_the_recorded_states()
    {
        var history = new UndoHistory();
        history.Reset("a");
        Assert.False(history.CanUndo);
        Assert.Null(history.Undo());

        Assert.True(history.Record("b", 10));
        Assert.True(history.Record("c", 20));
        Assert.False(history.Record("c", 30));   // nothing changed

        Assert.Equal("b", history.Undo());
        Assert.Equal("a", history.Undo());
        Assert.Null(history.Undo());
        Assert.Equal("b", history.Redo());
        Assert.Equal("c", history.Redo());
        Assert.Null(history.Redo());
    }

    [Fact]
    public void Quick_changes_are_one_undo_step()
    {
        var history = new UndoHistory();
        history.Reset("start");
        history.Record("drag 1", 10);
        history.Record("drag 2", 10.2);
        history.Record("drag 3", 10.4);
        history.Record("later", 12);

        Assert.Equal("drag 3", history.Undo());
        Assert.Equal("start", history.Undo());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_change_right_after_undo_is_its_own_step_and_clears_redo()
    {
        var history = new UndoHistory();
        history.Reset("a");
        history.Record("b", 10);
        Assert.Equal("a", history.Undo());
        history.Record("c", 10.1);

        Assert.False(history.CanRedo);
        Assert.Equal("a", history.Undo());
    }

    [Fact]
    public void History_keeps_only_the_newest_steps()
    {
        var history = new UndoHistory(limit: 3);
        history.Reset("0");
        for (int i = 1; i <= 10; i++) history.Record(i.ToString(), i * 10);

        Assert.Equal("9", history.Undo());
        Assert.Equal("8", history.Undo());
        Assert.Equal("7", history.Undo());
        Assert.Null(history.Undo());
    }

    [Fact]
    public void Groups_are_saved_and_forget_removed_fixtures()
    {
        var t = new TestShow();
        t.Show.Groups.Add(new FixtureGroup { Name = "Front", FixtureIds = { t.Par2.Id, t.Par1.Id }, Key = "F" });

        var loaded = ShowSerializer.Deserialize(ShowSerializer.SerializeCompact(t.Show));
        var group = Assert.Single(loaded.Groups);
        Assert.Equal("Front", group.Name);
        Assert.Equal("F", group.Key);
        Assert.Equal(new[] { t.Par2.Id, t.Par1.Id }, group.FixtureIds);

        loaded.RemoveFixture(t.Par2.Id);
        Assert.Equal(new[] { t.Par1.Id }, loaded.Groups[0].FixtureIds);
    }

    [Fact]
    public void Replacing_the_show_keeps_the_cue_position_and_Back_survives_a_shorter_list()
    {
        var t = new TestShow();
        var full = t.AddPreset("Full", (t.Par1, "Intensity", 255));
        t.AddCue(0, full);
        t.AddCue(0, full);
        t.AddCue(0, full);
        string before = ShowSerializer.SerializeCompact(t.Show);

        double now = 0;
        var engine = new LightingEngine(clock: () => now);
        engine.LoadShow(t.Show);
        engine.GoTo(2);
        Assert.Equal(2, engine.CuePlayer.CurrentIndex);

        var shorter = ShowSerializer.Deserialize(before);
        shorter.CueList.Cues.RemoveRange(1, 2);
        engine.ReplaceShow(shorter);
        Assert.Same(shorter, engine.Show);
        Assert.Equal(2, engine.CuePlayer.CurrentIndex);

        engine.Back();   // must not throw
        Assert.Equal(0, engine.CuePlayer.CurrentIndex);
    }

    [Theory]
    [InlineData("v1.6.0", 1, 6, 0)]
    [InlineData("1.6", 1, 6, 0)]
    [InlineData(" V2.0.1 ", 2, 0, 1)]
    public void ParseTag_reads_release_tags(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), UpdateCheck.ParseTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.5.0-preview.1")]
    [InlineData("latest")]
    public void ParseTag_ignores_everything_else(string? tag) => Assert.Null(UpdateCheck.ParseTag(tag));

    [Fact]
    public void Only_a_newer_full_release_counts_as_an_update()
    {
        var current = new Version(1, 5, 1, 0);
        const string page = "https://github.com/vothalvino/Lumeshow/releases/tag/v1.6.0";

        var update = UpdateCheck.FromJson($$"""{"tag_name":"v1.6.0","html_url":"{{page}}","draft":false,"prerelease":false}""", current);
        Assert.Equal(new UpdateInfo("1.6.0", page), update);

        Assert.Null(UpdateCheck.FromJson("""{"tag_name":"v1.5.1"}""", current));
        Assert.Null(UpdateCheck.FromJson("""{"tag_name":"v1.4.0"}""", current));
        Assert.Null(UpdateCheck.FromJson("""{"tag_name":"v1.6.0","prerelease":true}""", current));
        Assert.Null(UpdateCheck.FromJson("""{"tag_name":"v1.6.0","draft":true}""", current));
        Assert.Null(UpdateCheck.FromJson("""{"message":"API rate limit exceeded"}""", current));
        Assert.Null(UpdateCheck.FromJson("not json", current));
        Assert.Null(UpdateCheck.FromJson("[]", current));
    }

    [Fact]
    public void An_update_only_links_to_the_project_page()
    {
        var update = UpdateCheck.FromJson("""{"tag_name":"v9.0.0","html_url":"https://example.com/evil"}""", new Version(1, 5, 1));
        Assert.Equal(UpdateCheck.ReleasesPage, update!.Url);
    }
}
