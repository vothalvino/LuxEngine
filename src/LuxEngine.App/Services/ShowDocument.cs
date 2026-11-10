using LuxEngine.Core.Engine;
using LuxEngine.Core.Shows;

namespace LuxEngine.App.Services;

[Flags]
public enum ShowChange
{
    None = 0,
    Patch = 1,
    Presets = 2,
    Cues = 4,
    Outputs = 8,
    All = Patch | Presets | Cues | Outputs,

    /// <summary>Added when undo or redo put back an earlier version of the whole show.</summary>
    Restored = 16,
}

/// <summary>The open show: its file path, unsaved-changes flag, autosave and change notifications.</summary>
public sealed class ShowDocument
{
    private readonly LightingEngine _engine;
    private readonly UndoHistory _history = new();

    public ShowDocument(LightingEngine engine)
    {
        _engine = engine;
        ResetHistory();
    }

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public string? FilePath { get; private set; }
    public bool IsDirty { get; private set; }
    public Show Show => _engine.Show;

    public string DisplayName => FilePath is null ? Show.Name : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Raised on the UI thread after the show structure changed.</summary>
    public event Action<ShowChange>? Changed;

    /// <summary>Raised when the file name or dirty flag changed.</summary>
    public event Action? StateChanged;

    public static string AutosavePath => Path.Combine(AppPaths.DataFolder, "autosave" + ShowSerializer.FileExtension);

    /// <summary>Edits the show under the engine lock, marks it unsaved and notifies the UI.</summary>
    public void Edit(ShowChange change, Action<Show> edit)
    {
        _engine.Edit(edit);
        MarkDirty();
        Changed?.Invoke(change);
    }

    /// <summary>Call after changing the show directly: marks it unsaved and adds an undo step.</summary>
    public void MarkDirty()
    {
        bool undoChanged = _history.Record(Snapshot(), _engine.Now);
        if (IsDirty && !undoChanged) return;
        IsDirty = true;
        StateChanged?.Invoke();
    }

    /// <summary>Takes back the last change to the show. What is live (programmer, running cue) is left alone.</summary>
    public bool Undo() => Restore(_history.Undo());

    public bool Redo() => Restore(_history.Redo());

    private bool Restore(string? state)
    {
        if (state is null) return false;
        string outputsBefore;
        lock (_engine.SyncRoot) outputsBefore = System.Text.Json.JsonSerializer.Serialize(_engine.Show.Outputs, ShowSerializer.Options);
        var show = ShowSerializer.Deserialize(state);
        _engine.ReplaceShow(show);
        IsDirty = true;
        StateChanged?.Invoke();
        // Outputs are only set up again when they really changed, so undo never interrupts the DMX signal.
        bool outputsChanged = System.Text.Json.JsonSerializer.Serialize(show.Outputs, ShowSerializer.Options) != outputsBefore;
        Changed?.Invoke(ShowChange.Restored | ShowChange.Patch | ShowChange.Presets | ShowChange.Cues | (outputsChanged ? ShowChange.Outputs : ShowChange.None));
        return true;
    }

    private string Snapshot()
    {
        lock (_engine.SyncRoot) return ShowSerializer.SerializeCompact(_engine.Show);
    }

    private void ResetHistory() => _history.Reset(Snapshot());

    public void NotifyChanged(ShowChange change) => Changed?.Invoke(change);

    public void New()
    {
        _engine.LoadShow(Show.CreateNew());
        FilePath = null;
        IsDirty = false;
        ResetHistory();
        StateChanged?.Invoke();
        Changed?.Invoke(ShowChange.All);
    }

    public void Open(string path, bool keepPath = true)
    {
        var show = ShowSerializer.Load(path);
        _engine.LoadShow(show);
        FilePath = keepPath ? path : null;
        IsDirty = !keepPath;
        ResetHistory();
        StateChanged?.Invoke();
        Changed?.Invoke(ShowChange.All);
    }

    public void Save(string path)
    {
        Show show = _engine.Show;
        lock (_engine.SyncRoot)
        {
            // The show takes the file's name; if writing fails it keeps its old name.
            var oldName = show.Name;
            show.Name = Path.GetFileNameWithoutExtension(path);
            try
            {
                ShowSerializer.Save(show, path);
            }
            catch
            {
                show.Name = oldName;
                throw;
            }
        }
        FilePath = path;
        IsDirty = false;
        StateChanged?.Invoke();
    }

    /// <summary>Writes a recovery copy if there are unsaved changes.</summary>
    public void Autosave()
    {
        if (!IsDirty) return;
        try
        {
            lock (_engine.SyncRoot) ShowSerializer.Save(_engine.Show, AutosavePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Autosave is best effort; the next tick tries again.
        }
    }

    public static void DeleteAutosave()
    {
        try { File.Delete(AutosavePath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
