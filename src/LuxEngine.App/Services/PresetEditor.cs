using LuxEngine.Core.Engine;
using LuxEngine.Core.Presets;

namespace LuxEngine.App.Services;

/// <summary>
/// Editing an existing preset: its values are loaded into the controls, its lights are marked,
/// and Save or Cancel puts the previous live look back.
/// </summary>
public sealed class PresetEditor
{
    private ProgrammerSnapshot? _before;

    public Preset? Editing { get; private set; }

    public event Action? Changed;

    public bool IsEditing => Editing is not null;

    public void Begin(Guid presetId)
    {
        var preset = AppServices.Engine.Show.PresetRoot.FindPreset(presetId);
        if (preset is null) return;
        if (IsEditing) Cancel();

        _before = AppServices.Engine.BeginPresetEdit(preset);
        Editing = preset;
        AppServices.SelectPreset(preset.Id);
        AppServices.Selection.Set(preset.FixtureIds);
        AppServices.MainWindow?.ShowControlTab();
        Changed?.Invoke();
    }

    public void Save(string? color = null)
    {
        if (Editing is not { } preset || _before is null) return;
        AppServices.Document.Edit(ShowChange.Presets | ShowChange.Cues, show =>
        {
            if (color is not null) preset.Color = color;
            AppServices.Engine.EndPresetEdit(preset, _before, save: true);
            if (preset.AutoName) preset.Name = PresetNamer.Suggest(show, preset);
        });
        Finish();
    }

    public void Cancel()
    {
        if (Editing is not { } preset || _before is null) return;
        AppServices.Engine.EndPresetEdit(preset, _before, save: false);
        Finish();
    }

    /// <summary>Stops editing without touching the programmer, e.g. when another show was opened.</summary>
    public void Abandon()
    {
        if (!IsEditing) return;
        Editing = null;
        _before = null;
        Changed?.Invoke();
    }

    private void Finish()
    {
        Editing = null;
        _before = null;
        Changed?.Invoke();
    }
}
