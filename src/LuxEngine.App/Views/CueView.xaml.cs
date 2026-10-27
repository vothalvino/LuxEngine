using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Cues;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace LuxEngine.App.Views;

public sealed class CueRow : ObservableObject
{
    private string _number = "";
    private string _name = "";
    private string _presets = "";
    private string _timing = "";
    private Visibility _activeVisibility = Visibility.Collapsed;

    public CueRow(Guid id) => Id = id;

    public Guid Id { get; }
    public string Number { get => _number; set => SetProperty(ref _number, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Presets { get => _presets; set => SetProperty(ref _presets, value); }
    public string Timing { get => _timing; set => SetProperty(ref _timing, value); }
    public Visibility ActiveVisibility { get => _activeVisibility; set => SetProperty(ref _activeVisibility, value); }
}

public sealed class CuePresetRow
{
    public Guid PresetId { get; set; }
    public string Name { get; set; } = "";
    public Brush? ColorBrush { get; set; }
}

/// <summary>The cue list: each cue fades to a combination of presets.</summary>
public sealed partial class CueView : UserControl
{
    private readonly DispatcherQueueTimer _timer;
    private bool _loadingEditor;
    private int _lastActive = -2;

    public ObservableCollection<CueRow> Cues { get; } = new();
    public ObservableCollection<CuePresetRow> CuePresets { get; } = new();

    public CueView()
    {
        InitializeComponent();
        AppServices.Document.Changed += change =>
        {
            if (change.HasFlag(ShowChange.Cues) || change.HasFlag(ShowChange.Presets)) Refresh();
        };
        AppServices.SelectedPresetChanged += UpdateAddPresetButton;
        Loaded += (_, _) => Refresh();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += (_, _) => UpdatePlayback();
        _timer.Start();
    }

    private static CueList List => AppServices.Engine.Show.CueList;

    private Cue? SelectedCue =>
        CueListView.SelectedItem is CueRow row ? List.Cues.FirstOrDefault(c => c.Id == row.Id) : null;

    // ------------------------------------------------------------------ list

    public void Refresh()
    {
        var selectedId = (CueListView.SelectedItem as CueRow)?.Id;
        Cues.Clear();
        var root = AppServices.Engine.Show.PresetRoot;
        for (int i = 0; i < List.Cues.Count; i++)
        {
            var cue = List.Cues[i];
            var names = cue.PresetIds.Select(root.FindPreset).Where(p => p is not null).Select(p => p!.Name).ToList();
            Cues.Add(new CueRow(cue.Id)
            {
                Number = (i + 1).ToString(),
                Name = string.IsNullOrWhiteSpace(cue.Name) ? $"Cue {i + 1}" : cue.Name,
                Presets = names.Count == 0 ? "No presets yet — drag some here" : string.Join(" + ", names),
                Timing = Timing(cue),
            });
        }
        EmptyCues.Visibility = Cues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoopToggle.IsChecked = List.Loop;
        _lastActive = -2;

        var reselect = Cues.FirstOrDefault(r => r.Id == selectedId) ?? Cues.FirstOrDefault();
        CueListView.SelectedItem = reselect;
        LoadEditor();
    }

    private static string Timing(Cue cue)
    {
        var text = cue.FadeIn == cue.FadeOut ? $"{cue.FadeIn:0.#}s" : $"↑{cue.FadeIn:0.#}s ↓{cue.FadeOut:0.#}s";
        if (cue.Delay > 0) text += $" +{cue.Delay:0.#}s wait";
        if (cue.AutoFollow) text += " → auto";
        return text;
    }

    private void UpdatePlayback()
    {
        var player = AppServices.Engine.CuePlayer;
        FadeProgress.Value = player.CurrentCue is null && !player.IsFading ? 0 : player.Progress;
        PauseText.Text = player.IsPaused ? "Resume" : "Pause";
        PauseIcon.Glyph = player.IsPaused ? "" : "";

        if (player.CurrentIndex == _lastActive) return;
        _lastActive = player.CurrentIndex;
        for (int i = 0; i < Cues.Count; i++)
            Cues[i].ActiveVisibility = i == player.CurrentIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CueList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadEditor();

    /// <summary>Double-clicking a cue jumps straight to it.</summary>
    private void CueList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is CueRow row)
            AppServices.Engine.GoTo(Cues.IndexOf(row));
    }

    // ------------------------------------------------------------------ playback

    public void Go()
    {
        if (List.Cues.Count == 0)
        {
            AppServices.MainWindow?.ShowCueTab();
            return;
        }
        AppServices.Engine.Go();
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Go();
    private void Back_Click(object sender, RoutedEventArgs e) => AppServices.Engine.Back();
    private void Pause_Click(object sender, RoutedEventArgs e) => AppServices.Engine.TogglePause();
    private void Release_Click(object sender, RoutedEventArgs e) => AppServices.Engine.Release();

    // ------------------------------------------------------------------ editing the list

    private void AddCue_Click(object sender, RoutedEventArgs e)
    {
        var cue = new Cue { Name = $"Cue {List.Cues.Count + 1}" };
        var after = SelectedCue;
        AppServices.Document.Edit(ShowChange.Cues, show =>
        {
            int index = after is null ? show.CueList.Cues.Count : show.CueList.Cues.IndexOf(after) + 1;
            show.CueList.Cues.Insert(index, cue);
            if (AppServices.SelectedPresetId is Guid preset && show.PresetRoot.FindPreset(preset) is not null && after is null && show.CueList.Cues.Count == 1)
                cue.PresetIds.Add(preset);
        });
        CueListView.SelectedItem = Cues.FirstOrDefault(r => r.Id == cue.Id);
    }

    private async void DeleteCue_Click(object sender, RoutedEventArgs e)
    {
        var cue = SelectedCue;
        if (cue is null) return;
        if (!await DialogHelper.ConfirmAsync("Delete cue?", $"Delete \"{cue.Name}\"? The presets themselves are kept.")) return;
        AppServices.Document.Edit(ShowChange.Cues, show => show.CueList.Cues.Remove(cue));
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        var cue = SelectedCue;
        if (cue is null) return;
        int index = List.Cues.IndexOf(cue), target = index + delta;
        if (target < 0 || target >= List.Cues.Count) return;
        AppServices.Document.Edit(ShowChange.Cues, show =>
        {
            show.CueList.Cues.RemoveAt(index);
            show.CueList.Cues.Insert(target, cue);
        });
    }

    private void Loop_Click(object sender, RoutedEventArgs e)
    {
        bool loop = LoopToggle.IsChecked == true;
        AppServices.Document.Edit(ShowChange.None, show => show.CueList.Loop = loop);
    }

    // ------------------------------------------------------------------ editor

    private void LoadEditor()
    {
        var cue = SelectedCue;
        Editor.Visibility = cue is null ? Visibility.Collapsed : Visibility.Visible;
        NoCueSelected.Visibility = cue is null && Cues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CuePresets.Clear();
        if (cue is null) return;

        _loadingEditor = true;
        try
        {
            CueName.Text = cue.Name;
            FadeInBox.Value = cue.FadeIn;
            FadeOutBox.Value = cue.FadeOut;
            DelayBox.Value = cue.Delay;
            FollowSwitch.IsOn = cue.AutoFollow;
            FollowDelayBox.Value = cue.FollowDelay;
            FollowDelayBox.IsEnabled = cue.AutoFollow;

            var root = AppServices.Engine.Show.PresetRoot;
            foreach (var id in cue.PresetIds)
            {
                var preset = root.FindPreset(id);
                if (preset is null) continue;
                CuePresets.Add(new CuePresetRow
                {
                    PresetId = id,
                    Name = preset.Name,
                    ColorBrush = new SolidColorBrush(ColorMixer.Parse(preset.Color, Colors.LightGray)),
                });
            }
            DropHint.Visibility = CuePresets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _loadingEditor = false;
        }
        UpdateAddPresetButton();
    }

    private void UpdateAddPresetButton()
    {
        var preset = AppServices.SelectedPresetId is Guid id ? AppServices.Engine.Show.PresetRoot.FindPreset(id) : null;
        AddSelectedPresetButton.IsEnabled = preset is not null && SelectedCue is not null;
        AddSelectedPresetText.Text = preset is null ? "Click a preset on the left to add it" : $"Add \"{preset.Name}\" to this cue";
    }

    private void EditCue(Action<Cue> edit, bool refreshList = true)
    {
        var cue = SelectedCue;
        if (cue is null || _loadingEditor) return;
        AppServices.Engine.Edit(_ => edit(cue));
        AppServices.Document.MarkDirty();
        if (!refreshList) return;
        var row = Cues.FirstOrDefault(r => r.Id == cue.Id);
        if (row is null) return;
        row.Name = string.IsNullOrWhiteSpace(cue.Name) ? $"Cue {row.Number}" : cue.Name;
        row.Timing = Timing(cue);
        var root = AppServices.Engine.Show.PresetRoot;
        var names = cue.PresetIds.Select(root.FindPreset).Where(p => p is not null).Select(p => p!.Name).ToList();
        row.Presets = names.Count == 0 ? "No presets yet — drag some here" : string.Join(" + ", names);
    }

    private void CueName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SelectedCue is { } cue && cue.Name != CueName.Text) EditCue(c => c.Name = CueName.Text);
    }

    private void Timing_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        static double Safe(double v) => double.IsNaN(v) ? 0 : v;
        if (SelectedCue is not { } cue) return;
        if (cue.FadeIn == Safe(FadeInBox.Value) && cue.FadeOut == Safe(FadeOutBox.Value) &&
            cue.Delay == Safe(DelayBox.Value) && cue.FollowDelay == Safe(FollowDelayBox.Value)) return;
        EditCue(c =>
        {
            c.FadeIn = Safe(FadeInBox.Value);
            c.FadeOut = Safe(FadeOutBox.Value);
            c.Delay = Safe(DelayBox.Value);
            c.FollowDelay = Safe(FollowDelayBox.Value);
        });
    }

    private void Follow_Toggled(object sender, RoutedEventArgs e)
    {
        FollowDelayBox.IsEnabled = FollowSwitch.IsOn;
        if (SelectedCue is { } cue && cue.AutoFollow != FollowSwitch.IsOn) EditCue(c => c.AutoFollow = FollowSwitch.IsOn);
    }

    // ------------------------------------------------------------------ presets in the cue

    public void AddPresetToSelectedCue(Guid presetId)
    {
        var cue = SelectedCue;
        if (cue is null)
        {
            AddCue_Click(this, new RoutedEventArgs());
            cue = SelectedCue;
            if (cue is null) return;
        }
        if (cue.PresetIds.Contains(presetId)) return;
        EditCue(c => c.PresetIds.Add(presetId));
        LoadEditor();
        AppServices.MainWindow?.ShowCueTab();
    }

    private void AddSelectedPreset_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.SelectedPresetId is Guid id) AddPresetToSelectedCue(id);
    }

    private void RemovePreset_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Guid id) return;
        EditCue(c => c.PresetIds.Remove(id));
        LoadEditor();
    }

    private void PresetList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var order = CuePresets.Select(r => r.PresetId).ToList();
        EditCue(c => c.PresetIds = order);
    }

    private void PresetDrop_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to cue";
        }
    }

    private async void PresetList_Drop(object sender, DragEventArgs e)
    {
        var id = await DroppedPresetAsync(e);
        if (id is Guid presetId) AddPresetToSelectedCue(presetId);
    }

    /// <summary>Dropping a preset on a cue row adds it to that cue.</summary>
    private async void CueList_Drop(object sender, DragEventArgs e)
    {
        var id = await DroppedPresetAsync(e);
        if (id is not Guid presetId) return;

        // Find the row under the pointer; fall back to the selected cue.
        var position = e.GetPosition(CueListView);
        foreach (var row in Cues)
        {
            if (CueListView.ContainerFromItem(row) is not ListViewItem item) continue;
            var transform = item.TransformToVisual(CueListView).TransformPoint(new Windows.Foundation.Point(0, 0));
            if (position.Y >= transform.Y && position.Y <= transform.Y + item.ActualHeight)
            {
                CueListView.SelectedItem = row;
                break;
            }
        }
        AddPresetToSelectedCue(presetId);
    }

    private static async Task<Guid?> DroppedPresetAsync(DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return null;
        var text = await e.DataView.GetTextAsync();
        return text.StartsWith(PresetPanel.DragPrefix) && Guid.TryParse(text[PresetPanel.DragPrefix.Length..], out var id) ? id : null;
    }
}
