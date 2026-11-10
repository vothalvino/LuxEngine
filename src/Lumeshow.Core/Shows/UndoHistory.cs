namespace Lumeshow.Core.Shows;

/// <summary>
/// Undo and redo for the show, kept as snapshots (the show as text). Call <see cref="Record"/> after
/// every change; changes that follow each other quickly (dragging, typing) count as one step.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>Changes closer together than this many seconds are one undo step.</summary>
    public const double MergeSeconds = 0.6;

    private readonly int _limit;
    private readonly LinkedList<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string _current = "";
    private double _lastRecord = double.NegativeInfinity;

    public UndoHistory(int limit = 100) => _limit = limit;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Starts again from this state, e.g. after opening a show.</summary>
    public void Reset(string state)
    {
        _undo.Clear();
        _redo.Clear();
        _current = state;
        _lastRecord = double.NegativeInfinity;
    }

    /// <summary>Notes the state after a change. Returns false when nothing actually changed.</summary>
    public bool Record(string state, double now)
    {
        if (state == _current) return false;
        bool merge = _undo.Count > 0 && _redo.Count == 0 && now - _lastRecord < MergeSeconds;
        if (!merge)
        {
            _undo.AddLast(_current);
            if (_undo.Count > _limit) _undo.RemoveFirst();
        }
        _redo.Clear();
        _current = state;
        _lastRecord = now;
        return true;
    }

    /// <summary>Steps back; returns the state to restore, or null when there is nothing to undo.</summary>
    public string? Undo()
    {
        if (_undo.Last is null) return null;
        _redo.Push(_current);
        _current = _undo.Last.Value;
        _undo.RemoveLast();
        _lastRecord = double.NegativeInfinity;
        return _current;
    }

    public string? Redo()
    {
        if (_redo.Count == 0) return null;
        _undo.AddLast(_current);
        _current = _redo.Pop();
        _lastRecord = double.NegativeInfinity;
        return _current;
    }
}
