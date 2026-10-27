using LuxEngine.Core.Fixtures;

namespace LuxEngine.Core.Tests;

public class BundledFixtureTests
{
    private static string FixtureFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LuxEngine.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found"), "fixtures");
    }

    [Fact]
    public void Bundled_library_loads_without_errors()
    {
        var library = new FixtureLibrary();
        var errors = library.LoadFolder(FixtureFolder());

        Assert.Empty(errors);
        Assert.True(library.All.Count > 300, $"Only {library.All.Count} fixtures loaded");
        Assert.All(library.All, def =>
        {
            Assert.NotEmpty(def.Modes);
            foreach (var mode in def.Modes)
            foreach (var name in mode.Channels.OfType<string>())
                Assert.NotNull(def.FindChannel(name));
        });
    }

    [Fact]
    public void Bundled_library_has_common_moving_heads_and_pars()
    {
        var library = new FixtureLibrary();
        library.LoadFolder(FixtureFolder());

        Assert.NotEmpty(library.Search("chauvet par"));
        var mover = library.Search(null, "Moving Head").First();
        Assert.Contains(mover.Channels, c => c.Type == ChannelType.Pan);
    }
}
