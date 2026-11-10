namespace LuxEngine.Core.Fixtures;

/// <summary>A saved selection of lights, such as "Front pars", recalled with one click or key.</summary>
public sealed class FixtureGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New group";

    /// <summary>The lights, in the order they were selected (effects use this order).</summary>
    public List<Guid> FixtureIds { get; set; } = new();

    /// <summary>Keyboard key that selects this group (a VirtualKey name such as "G"), or null.</summary>
    public string? Key { get; set; }
}
