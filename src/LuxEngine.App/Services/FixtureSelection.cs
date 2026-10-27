namespace LuxEngine.App.Services;

/// <summary>The fixtures currently selected for the Control view, shared by the list and the stage.</summary>
public sealed class FixtureSelection
{
    private readonly List<Guid> _ids = new();

    /// <summary>Selected ids in the order they were selected (effects use this order).</summary>
    public IReadOnlyList<Guid> Ids => _ids;

    public int Count => _ids.Count;

    public event Action? Changed;

    public bool Contains(Guid id) => _ids.Contains(id);

    public void Set(IEnumerable<Guid> ids)
    {
        var next = ids.Distinct().ToList();
        if (next.SequenceEqual(_ids)) return;
        _ids.Clear();
        _ids.AddRange(next);
        Changed?.Invoke();
    }

    public void Toggle(Guid id)
    {
        if (!_ids.Remove(id)) _ids.Add(id);
        Changed?.Invoke();
    }

    public void Clear() => Set(Array.Empty<Guid>());

    /// <summary>Drops ids of fixtures that no longer exist.</summary>
    public void Retain(IEnumerable<Guid> existing)
    {
        var keep = existing.ToHashSet();
        if (_ids.RemoveAll(id => !keep.Contains(id)) > 0) Changed?.Invoke();
    }
}
