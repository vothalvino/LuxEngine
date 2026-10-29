using LuxEngine.App.Dialogs;
using LuxEngine.App.Services;
using LuxEngine.App.Views;
using LuxEngine.Core.Cues;
using LuxEngine.Core.Fixtures;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace LuxEngine.App;

/// <summary>One keyboard shortcut: the keys, where it is listed and what it does.</summary>
/// <param name="Run">Returns false when the key should be left alone (e.g. a parameter key with nothing selected).</param>
public sealed record Shortcut(string Keys, string Group, string Description, VirtualKey Key, bool Ctrl, bool Shift, Func<bool> Run, bool Repeats = false);

public sealed partial class MainWindow
{
    private const VirtualKey OpenBracket = (VirtualKey)219, CloseBracket = (VirtualKey)221, QuestionMark = (VirtualKey)191;

    private List<Shortcut>? _shortcuts;
    private bool _highlighting;

    /// <summary>Every built-in shortcut. The key handler and the shortcut sheet both use this list.</summary>
    public IReadOnlyList<Shortcut> Shortcuts => _shortcuts ??= BuildShortcuts();

    private List<Shortcut> BuildShortcuts()
    {
        var list = new List<Shortcut>();
        void Add(string keys, string group, string description, VirtualKey key, bool ctrl, bool shift, Func<bool> run, bool repeats = false) =>
            list.Add(new Shortcut(keys, group, description, key, ctrl, shift, run, repeats));
        bool Do(Action action) { action(); return true; }

        // Parameters: only with lights selected (otherwise the key is left alone).
        foreach (var p in ParameterBar.Parameters)
            Add(p.Keys, "Parameters (select lights first)", p.Kind == ParameterBar.Kind.Position ? "Position: ← → pan, ↑ ↓ tilt" : p.Name,
                p.Key, false, true, () => ParamBar.TryOpen(p.Key));

        // Selection
        Add("Ctrl+A", "Selection", "Select all lights", VirtualKey.A, true, false, () => Do(() => AppServices.Selection.Set(AppServices.Engine.Show.Fixtures.OrderByStage().Select(f => f.Id))));
        Add("Ctrl+Shift+A", "Selection", "Select all lights of the same type", VirtualKey.A, true, true, () => Do(SelectSameType));
        Add("Ctrl+I", "Selection", "Invert the selection", VirtualKey.I, true, false, () => Do(() =>
            AppServices.Selection.Set(AppServices.Engine.Show.Fixtures.OrderByStage().Select(f => f.Id).Where(id => !AppServices.Selection.Contains(id)))));
        Add("]", "Selection", "Next light (stage order, left to right)", CloseBracket, false, false, () => Do(() => StepSelection(+1, add: false)), repeats: true);
        Add("[", "Selection", "Previous light", OpenBracket, false, false, () => Do(() => StepSelection(-1, add: false)), repeats: true);
        Add("Shift+]", "Selection", "Add the next light", CloseBracket, false, true, () => Do(() => StepSelection(+1, add: true)), repeats: true);
        Add("Shift+[", "Selection", "Add the previous light", OpenBracket, false, true, () => Do(() => StepSelection(-1, add: true)), repeats: true);
        Add("H", "Selection", "Highlight the selected lights (full, open white, centred) on/off", VirtualKey.H, false, false, () => Do(ToggleHighlight));
        Add("Esc", "Selection", "Close the parameter bar, cancel editing a preset, or deselect", VirtualKey.Escape, false, false, () => Do(Escape));

        // Playback
        Add("Space", "Playback", "GO (next cue)", VirtualKey.Space, false, false, () => Do(CueView.Go));
        Add("Shift+Space", "Playback", "Back (previous cue)", VirtualKey.Space, false, true, () => Do(AppServices.Engine.Back));
        Add("P", "Playback", "Pause or resume the cue fade", VirtualKey.P, false, false, () => Do(AppServices.Engine.TogglePause));
        Add("Ctrl+Backspace", "Playback", "Release everything (cue list and all buttons)", VirtualKey.Back, true, false, () => Do(() =>
        {
            AppServices.Engine.Release();
            AppServices.Engine.ReleaseButtons();
        }));
        Add("B", "Playback", "Blackout on/off", VirtualKey.B, false, false, () => Do(() =>
        {
            AppServices.Engine.Blackout = !AppServices.Engine.Blackout;
            SyncLiveControls();
        }));
        Add("Ctrl+↑", "Playback", "Master fader up 5%", VirtualKey.Up, true, false, () => Do(() => NudgeMaster(+0.05)), repeats: true);
        Add("Ctrl+↓", "Playback", "Master fader down 5%", VirtualKey.Down, true, false, () => Do(() => NudgeMaster(-0.05)), repeats: true);

        // Navigation and editing
        Add("F1 or ?", "Navigation", "This list of shortcuts", VirtualKey.F1, false, false, () => Do(() => _ = ShortcutsDialog.ShowAsync(this)));
        list.Add(list[^1] with { Keys = "?", Key = QuestionMark, Shift = true });
        Add("F2", "Navigation", "Control tab", VirtualKey.F2, false, false, () => Do(ShowControlTab));
        Add("F3", "Navigation", "Cue list", VirtualKey.F3, false, false, () => Do(() => { ShowCueTab(); CueView.ShowCueList(); }));
        Add("F4", "Navigation", "Cue buttons", VirtualKey.F4, false, false, () => Do(() => { ShowCueTab(); CueView.ShowButtons(); }));
        Add("Ctrl+F", "Navigation", "Search presets", VirtualKey.F, true, false, () => Do(PresetPanel.FocusSearch));
        Add("Ctrl+R", "Navigation", "Save the current look as a preset", VirtualKey.R, true, false, () => Do(() => _ = PresetPanel.RecordPresetAsync()));
        Add("Ctrl+Enter", "Navigation", "Save the preset you are editing", VirtualKey.Enter, true, false, () =>
        {
            if (!AppServices.PresetEditor.IsEditing) return false;
            AppServices.PresetEditor.Save();
            return true;
        });
        return list;
    }

    // ------------------------------------------------------------------ key handling

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private bool TypingInTextBox()
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        return focused is TextBox or PasswordBox or AutoSuggestBox or RichEditBox or NumberBox;
    }

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TypingInTextBox()) return;

        // Alt or F10 opens the menu bar on purpose; any other key while it holds the focus (e.g. after
        // clicking a menu) would make it drop a menu, so give the focus back to the main area first.
        if (e.Key is VirtualKey.Menu or VirtualKey.F10) _menuByKeyboard = true;
        else if (!_menuByKeyboard && IsInMenuBar(FocusManager.GetFocusedElement(Content.XamlRoot)))
            FocusMainArea();

        bool ctrl = IsDown(VirtualKey.Control), shift = IsDown(VirtualKey.Shift);
        bool repeat = e.KeyStatus.WasKeyDown;

        // 1. The parameter bar gets its keys first while it is open (arrows, digits, Enter, Esc…).
        if (ParamBar.IsOpen && !shift && ParamBar.HandleKey(e.Key, ctrl))
        {
            e.Handled = true;
            return;
        }

        // 2. Built-in shortcuts.
        var shortcut = Shortcuts.FirstOrDefault(s => s.Key == e.Key && s.Ctrl == ctrl && s.Shift == shift);
        if (shortcut is not null)
        {
            if (repeat && !shortcut.Repeats) { e.Handled = true; return; }
            if (shortcut.Run()) { e.Handled = true; return; }
        }

        // 3. Keys given to presets and cue buttons.
        if (!shift && !repeat && UseOwnKey(e.Key, ctrl, down: true)) e.Handled = true;
    }

    private void Root_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (TypingInTextBox()) return;
        if (UseOwnKey(e.Key, ctrl: false, down: false)) e.Handled = true;
    }

    /// <summary>A key the user gave to a cue button or preset. Flash buttons are on only while the key is held.</summary>
    private bool UseOwnKey(VirtualKey key, bool ctrl, bool down)
    {
        if (!KeyNames.IsAssignable(key)) return false;
        var name = key.ToString();
        var show = AppServices.Engine.Show;

        var button = show.ButtonPage.Buttons.FirstOrDefault(b => b.Key == name);
        if (button is not null)
        {
            if (button.Mode == CueButtonMode.Flash) AppServices.Engine.SetButton(button.Id, down);
            else if (down) AppServices.Engine.ToggleButton(button.Id);
            return true;
        }
        if (!down) return false;
        var preset = show.PresetRoot.AllPresets().FirstOrDefault(p => p.Key == name);
        if (preset is null) return false;
        PresetPanel.UsePreset(preset.Id, layer: ctrl);
        return true;
    }

    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!ParamBar.IsOpen) return;
        ParamBar.Wheel(e.GetCurrentPoint(Root).Properties.MouseWheelDelta);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ actions

    private void StepSelection(int direction, bool add)
    {
        var order = AppServices.Engine.Show.Fixtures.OrderByStage().Select(f => f.Id).ToList();
        if (order.Count == 0) return;
        var ids = AppServices.Selection.Ids;
        int from = ids.Count == 0 ? (direction > 0 ? -1 : order.Count) : order.IndexOf(ids[^1]);
        int next = ((from + direction) % order.Count + order.Count) % order.Count;   // wraps around the stage
        AppServices.Selection.Set(add ? ids.Append(order[next]) : new[] { order[next] });
    }

    private static void SelectSameType()
    {
        var show = AppServices.Engine.Show;
        var types = AppServices.Selection.Ids.Select(show.FindFixture).OfType<FixtureInstance>().Select(f => f.DefinitionId).ToHashSet();
        AppServices.Selection.Set(show.Fixtures.Where(f => types.Contains(f.DefinitionId)).OrderByStage().Select(f => f.Id));
    }

    private void ToggleHighlight()
    {
        _highlighting = !_highlighting;
        if (_highlighting) AppServices.Engine.SetHighlight(AppServices.Selection.Ids);
        else AppServices.Engine.ClearHighlight();
        ParamBar.ShowMessage(_highlighting ? "Highlight on — press H again to turn it off" : "Highlight off");
    }

    private void OnSelectionChangedForHighlight()
    {
        // The highlight follows the selection, so ] and [ walk the highlight from light to light.
        if (_highlighting) AppServices.Engine.SetHighlight(AppServices.Selection.Ids);
    }

    private async void Escape()
    {
        if (AppServices.PresetEditor.IsEditing)
        {
            if (await Helpers.DialogHelper.ConfirmAsync("Stop editing the preset?",
                    $"Discard the changes to \"{AppServices.PresetEditor.Editing!.Name}\"?", "Discard changes"))
                AppServices.PresetEditor.Cancel();
            return;
        }
        AppServices.Selection.Clear();
    }

    private void NudgeMaster(double amount)
    {
        AppServices.Engine.GrandMaster = Math.Clamp(AppServices.Engine.GrandMaster + amount, 0, 1);
        SyncLiveControls();
    }

    /// <summary>Connects the parameter bar to the Control tab and the highlight to the selection; called once from the constructor.</summary>
    private bool _menuByKeyboard;

    private static bool IsInMenuBar(object? element)
    {
        for (var e = element as DependencyObject; e is not null; e = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(e))
            if (e is MenuBar) return true;
        return false;
    }

    /// <summary>Puts the keyboard focus on the stage, so shortcuts never land on the menu bar.</summary>
    private void FocusMainArea() => Stage.Focus(FocusState.Programmatic);

    private void SetUpKeyboard()
    {
        // The Control / Cue area keeps the size it was dragged to.
        BottomRow.Height = new GridLength(Math.Clamp(AppServices.Settings.BottomHeight, 220, 900));
        BottomSplitter.Target = BottomRow;
        BottomSplitter.Resized += height =>
        {
            AppServices.Settings.BottomHeight = height;
            AppServices.Settings.Save();
        };

        // The stage takes the keyboard focus at start-up instead of the first menu ("File").
        Stage.IsTabStop = true;
        Stage.UseSystemFocusVisuals = false;
        Root.Loaded += (_, _) => FocusMainArea();
        TopMenu.LosingFocus += (_, args) =>
        {
            if (!IsInMenuBar(args.NewFocusedElement)) _menuByKeyboard = false;
        };
        // After a menu or a dialog opened from it closes, the focus comes back to the menu title. Hand it
        // to the stage once nothing is open any more, so the next shortcut cannot drop a menu.
        TopMenu.GotFocus += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_menuByKeyboard) return;
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Content.XamlRoot).Count > 0) return;
            if (IsInMenuBar(FocusManager.GetFocusedElement(Content.XamlRoot))) FocusMainArea();
        });
        ParamBar.GetDimmerPercent = () => ControlView.DimmerPercent;
        ParamBar.SetDimmerPercent = v => ControlView.DimmerPercent = v;
        ParamBar.ValuesChanged += ControlView.RefreshValues;
        AppServices.Selection.Changed += OnSelectionChangedForHighlight;
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Root_PointerWheelChanged), true);
    }
}
