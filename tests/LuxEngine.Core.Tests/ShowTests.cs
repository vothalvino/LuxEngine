using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Presets;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Tests;

public class ShowTests
{
    [Fact]
    public void AddFixtures_places_fixtures_back_to_back_and_detects_conflicts()
    {
        var t = new TestShow();
        Assert.Equal(1, t.Par1.Address);
        Assert.Equal(5, t.Par2.Address);
        Assert.Equal(9, t.NoDimmer.Address);
        Assert.Empty(PatchHelper.FindConflicts(t.Show));

        t.Par2.Address = 3;
        var conflict = Assert.Single(PatchHelper.FindConflicts(t.Show));
        Assert.Equal(t.Par1, conflict.First);
    }

    [Fact]
    public void Fixtures_that_do_not_fit_move_to_next_universe()
    {
        var show = new Show();
        var big = FixtureLibrary.CreateGeneric("Big", Enumerable.Repeat(ChannelType.Generic, 300).ToArray());
        var added = PatchHelper.AddFixtures(show, big, big.Modes[0].Name, 2, 1, null);
        Assert.Equal((1, 1), (added[0].Universe, added[0].Address));
        Assert.Equal((2, 1), (added[1].Universe, added[1].Address));
    }

    [Fact]
    public void Save_and_load_round_trip_keeps_everything()
    {
        var t = new TestShow();
        var folder = new PresetFolder { Name = "Colors" };
        var preset = new Preset { Name = "Blue", Color = "#0000FF" };
        preset.Values.Add(new PresetValue(t.Par1.Id, "Blue", 255));
        preset.Effects.Add(new Effects.Effect { Type = Effects.EffectType.Rainbow, FixtureIds = { t.Par1.Id } });
        folder.Presets.Add(preset);
        t.Show.PresetRoot.Folders.Add(folder);
        t.AddCue(3, preset).AutoFollow = true;
        t.Show.Outputs.Add(new OutputConfig { Type = OutputType.Sacn, NetworkUniverse = 7 });

        var path = Path.Combine(Path.GetTempPath(), $"lux-{Guid.NewGuid()}.luxshow");
        try
        {
            ShowSerializer.Save(t.Show, path);
            var loaded = ShowSerializer.Load(path);

            Assert.Equal(3, loaded.Fixtures.Count);
            Assert.Equal(2, loaded.FixtureDefinitions.Count);
            var loadedPreset = Assert.Single(Assert.Single(loaded.PresetRoot.Folders).Presets);
            Assert.Equal(preset.Id, loadedPreset.Id);
            Assert.Equal(255, loadedPreset.Values[0].Value);
            Assert.Equal(Effects.EffectType.Rainbow, loadedPreset.Effects[0].Type);
            var cue = Assert.Single(loaded.CueList.Cues);
            Assert.True(cue.AutoFollow);
            Assert.Equal(preset.Id, cue.PresetIds[0]);
            Assert.Equal(OutputType.Sacn, loaded.Outputs[0].Type);
            Assert.Equal(ChannelType.Intensity, loaded.FixtureDefinitions[t.Par1.DefinitionId].Channels[0].Type);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Removing_a_folder_removes_its_presets_from_cues()
    {
        var t = new TestShow();
        var folder = new PresetFolder { Name = "F" };
        var inner = new PresetFolder { Name = "Inner" };
        var preset = new Preset();
        inner.Presets.Add(preset);
        folder.Folders.Add(inner);
        t.Show.PresetRoot.Folders.Add(folder);
        var cue = t.AddCue(1, preset);

        t.Show.RemovePresetOrFolder(folder.Id);

        Assert.Empty(t.Show.PresetRoot.Folders);
        Assert.Empty(cue.PresetIds);
    }

    [Fact]
    public void Library_search_matches_all_words()
    {
        var lib = new FixtureLibrary();
        lib.Add(new FixtureDefinition { Id = "a/slimpar", Manufacturer = "Chauvet DJ", Model = "SlimPAR 56", Category = "Color Changer" });
        lib.Add(new FixtureDefinition { Id = "b/spot", Manufacturer = "Robe", Model = "Spot 250", Category = "Moving Head" });

        Assert.Equal("a/slimpar", Assert.Single(lib.Search("chauvet par")).Id);
        Assert.Equal(2, lib.Search("").Count());
        Assert.Single(lib.Search(null, "Moving Head"));
    }
}
