using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Presets;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace LuxEngine.App.Views;

/// <summary>One row in the preset tree: a folder or a preset.</summary>
public sealed class PresetNode : ObservableObject
{
    private string _name = "";
    private bool _isExpanded = true;
    private string _badge = "";
    private string _detail = "";
    private Brush _accentBrush = new SolidColorBrush(Colors.Gray);

    public PresetNode(Guid id, bool isFolder)
    {
        Id = id;
        IsFolder = isFolder;
    }

    public Guid Id { get; }
    public bool IsFolder { get; }
    public ObservableCollection<PresetNode> Children { get; } = new();

    public string Glyph => IsFolder ? "" : "";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public string Badge { get => _badge; set => SetProperty(ref _badge, value); }
    public string Detail { get => _detail; set => SetProperty(ref _detail, value); }
    public Brush AccentBrush { get => _accentBrush; set => SetProperty(ref _accentBrush, value); }
}

public sealed partial class PresetPanel : UserControl
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _saveTimer;
    public const string DragPrefix = "lux-preset:";

    private readonly HashSet<Guid> _collapsed = new();
    private Guid? _currentFolder;
    private bool _restoringExpansion;

    public ObservableCollection<PresetNode> Nodes { get; } = new();

    public PresetPanel()
    {
        InitializeComponent();
        // Kept in a field: a timer only referenced locally can be garbage collected and stop ticking.
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(200);
        _saveTimer.Tick += (_, _) => ControlView.UpdateSaveButton(RecordButton);
        _saveTimer.Start();
        AppServices.Document.Changed += change =>
        {
            if (change.HasFlag(ShowChange.Presets) || change.HasFlag(ShowChange.Patch)) Refresh();
        };
        Loaded += (_, _) => Refresh();
    }

    // ------------------------------------------------------------------ building the tree

    public void Refresh()
    {
        Nodes.Clear();
        var root = AppServices.Engine.Show.PresetRoot;
        var query = SearchBox?.Text?.Trim() ?? "";

        if (query.Length > 0)
        {
            foreach (var preset in root.AllPresets().Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                Nodes.Add(CreatePresetNode(preset));
            Tree.CanReorderItems = false;
            Tree.CanDragItems = true;
            return;
        }

        Tree.CanReorderItems = true;
        foreach (var node in BuildChildren(root)) Nodes.Add(node);

        // TreeView doesn't always honour IsExpanded for items realized during the first layout,
        // so open the folders again once the containers exist.
        _restoringExpansion = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            foreach (var node in Nodes) Expand(node);
            _restoringExpansion = false;
        });
    }

    private void Expand(PresetNode node)
    {
        if (!node.IsFolder || !node.IsExpanded) return;
        if (Tree.ContainerFromItem(node) is TreeViewItem item) item.IsExpanded = true;
        foreach (var child in node.Children) Expand(child);
    }

    private IEnumerable<PresetNode> BuildChildren(PresetFolder folder)
    {
        foreach (var sub in folder.Folders)
        {
            var node = new PresetNode(sub.Id, isFolder: true)
            {
                Name = sub.Name,
                IsExpanded = !_collapsed.Contains(sub.Id),
                AccentBrush = (Brush)Application.Current.Resources["LuxAccentBrush"],
                Badge = sub.AllPresets().Count() is var n && n > 0 ? n.ToString() : "",
                Detail = $"Folder with {sub.AllPresets().Count()} preset(s)",
            };
            foreach (var child in BuildChildren(sub)) node.Children.Add(child);
            yield return node;
        }
        foreach (var preset in folder.Presets) yield return CreatePresetNode(preset);
    }

    private static PresetNode CreatePresetNode(Preset preset)
    {
        int fixtures = preset.FixtureIds.Count();
        var detail = $"{fixtures} fixture(s), {preset.Values.Count} value(s)";
        if (preset.Effects.Count > 0) detail += ", effects: " + string.Join(", ", preset.Effects.Select(e => e.Type));
        return new PresetNode(preset.Id, isFolder: false)
        {
            Name = preset.Name,
            AccentBrush = new SolidColorBrush(ColorMixer.Parse(preset.Color, Colors.LightGray)),
            Badge = string.Join("  ", new[] { preset.Effects.Count > 0 ? "FX" : "", preset.Key is null ? "" : $"[{KeyNames.Display(preset.Key)}]" }.Where(b => b.Length > 0)),
            Detail = detail,
        };
    }

    private void Tree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is not PresetNode node) return;
        node.IsExpanded = true;
        _collapsed.Remove(node.Id);
    }

    private void Tree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Item is not PresetNode node || _restoringExpansion) return;
        node.IsExpanded = false;
        _collapsed.Add(node.Id);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Refresh();

    // ------------------------------------------------------------------ actions

    private PresetFolder TargetFolder()
    {
        var root = AppServices.Engine.Show.PresetRoot;
        if (_currentFolder is Guid id)
        {
            var folder = id == root.Id ? root : root.AllFolders().FirstOrDefault(f => f.Id == id);
            if (folder is not null) return folder;
        }
        return root;
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = await DialogHelper.PromptAsync("New folder", "Folder name", "New folder", "Create");
        if (name is null) return;
        var target = TargetFolder();
        var folder = new PresetFolder { Name = name };
        AppServices.Document.Edit(ShowChange.Presets, _ => target.Folders.Add(folder));
        _currentFolder = folder.Id;
    }

    private async void Record_Click(object sender, RoutedEventArgs e) => await RecordPresetAsync();

    public async Task RecordPresetAsync()
    {
        var engine = AppServices.Engine;
        if (engine.Programmer.IsEmpty)
        {
            await DialogHelper.ShowMessageAsync("Nothing to save yet",
                "Select some fixtures and set colors, dimmer or position in the Control tab first. Then click \"+ Preset\" to save that look.");
            return;
        }

        var target = TargetFolder();
        var selected = AppServices.Selection.Ids;
        var preset = engine.RecordPreset("", selected.Count > 0 ? selected : null);
        var suggestion = PresetNamer.Suggest(engine.Show, preset);
        var name = await DialogHelper.PromptAsync("Save preset",
            $"Preset name (saved in \"{target.Name}\"). The suggested name describes what it contains; keep it and it updates when you change the preset.",
            suggestion, "Save");
        if (name is null) return;

        preset.Name = name.Trim().Length == 0 ? suggestion : name.Trim();
        preset.AutoName = preset.Name == suggestion;
        preset.Color = GuessColor(preset);
        AppServices.Document.Edit(ShowChange.Presets, _ => target.Presets.Add(preset));
        AppServices.SelectPreset(preset.Id);
    }

    /// <summary>Uses the color of the first fixture in the preset as the preset's accent color.</summary>
    private static string? GuessColor(Preset preset)
    {
        foreach (var id in preset.FixtureIds)
        {
            var look = AppServices.Engine.GetLook(id);
            if (look.Red + look.Green + look.Blue > 0.1)
                return ColorMixer.ToHex(ColorMixer.FromLook(look with { Intensity = 1 }));
        }
        return null;
    }

    private void Tree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not PresetNode node) return;
        if (node.IsFolder)
        {
            _currentFolder = node.Id;
            return;
        }
        ApplyPreset(node.Id, layer: CtrlDown());
    }

    private static bool CtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>Double-clicking a preset opens it for editing.</summary>
    public void FocusSearch() => SearchBox.Focus(FocusState.Keyboard);

    /// <summary>Used by keyboard keys: shows the preset, or adds it on top (Ctrl).</summary>
    public static void UsePreset(Guid id, bool layer) => ApplyPreset(id, layer);

    private void Tree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is PresetNode { IsFolder: false } node)
            AppServices.PresetEditor.Begin(node.Id);
    }

    /// <summary>
    /// Clicking a preset shows just that preset, so you see exactly what it does. Ctrl+click adds it on
    /// top of the current look instead, and while editing a preset clicks always add to it.
    /// </summary>
    private static void ApplyPreset(Guid id, bool layer = false)
    {
        var show = AppServices.Engine.Show;
        var preset = show.PresetRoot.FindPreset(id);
        if (preset is null) return;
        if (layer || AppServices.PresetEditor.IsEditing)
        {
            AppServices.Engine.ApplyPreset(preset);
            AppServices.Selection.Set(AppServices.Selection.Ids.Concat(preset.FixtureIds));
        }
        else
        {
            AppServices.Engine.ShowPreset(preset);
            AppServices.Selection.Set(preset.FixtureIds);
        }
        AppServices.SelectPreset(id);
    }

    private void Tree_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not PresetNode node) return;
        var menu = new MenuFlyout();

        void Add(string text, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (node.IsFolder)
        {
            Add("New folder inside", "", async () =>
            {
                _currentFolder = node.Id;
                var name = await DialogHelper.PromptAsync("New folder", "Folder name", "New folder", "Create");
                if (name is null) return;
                var parent = TargetFolder();
                AppServices.Document.Edit(ShowChange.Presets, _ => parent.Folders.Add(new PresetFolder { Name = name }));
            });
            Add("Save current look here", "", async () =>
            {
                _currentFolder = node.Id;
                await RecordPresetAsync();
            });
        }
        else
        {
            Add("Edit preset…", "\uE70F", () => AppServices.PresetEditor.Begin(node.Id));
            Add("Add on top of the current look", "\uE710", () => ApplyPreset(node.Id, layer: true));
            Add("Use now", "", () => ApplyPreset(node.Id));
            Add("Add to selected cue", "", () => AppServices.MainWindow?.Cues.AddPresetToSelectedCue(node.Id));
            Add("Update with current look", "", () => UpdatePreset(node.Id));
            Add("Use automatic name", "\uE8AC", () => AppServices.Document.Edit(ShowChange.Presets | ShowChange.Cues, show =>
            {
                var preset = show.PresetRoot.FindPreset(node.Id);
                if (preset is null) return;
                preset.Name = PresetNamer.Suggest(show, preset);
                preset.AutoName = true;
            }));
            Add("Set keyboard key…", "\uE92E", async () => await SetKeyAsync(node.Id));
            Add("Duplicate", "", () => DuplicatePreset(node.Id));
        }

        Add("Rename", "", async () =>
        {
            var name = await DialogHelper.PromptAsync("Rename", "New name", node.Name, "Rename");
            if (name is null) return;
            AppServices.Document.Edit(ShowChange.Presets | ShowChange.Cues, show =>
            {
                var preset = show.PresetRoot.FindPreset(node.Id);
                if (preset is not null)
                {
                    preset.Name = name;
                    preset.AutoName = false;   // the user's own name is kept from now on
                }
                var folder = show.PresetRoot.AllFolders().FirstOrDefault(f => f.Id == node.Id);
                if (folder is not null) folder.Name = name;
            });
        });

        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Delete", "", async () =>
        {
            var what = node.IsFolder ? $"the folder \"{node.Name}\" and all presets in it" : $"the preset \"{node.Name}\"";
            if (!await DialogHelper.ConfirmAsync("Delete?", $"Delete {what}? Cues that use them will no longer include them.")) return;
            AppServices.Document.Edit(ShowChange.Presets | ShowChange.Cues, show => show.RemovePresetOrFolder(node.Id));
        });

        menu.ShowAt((FrameworkElement)e.OriginalSource, e.GetPosition((FrameworkElement)e.OriginalSource));
        e.Handled = true;
    }

    /// <summary>Gives a preset its own key: pressing it shows the preset (Ctrl+key adds it on top).</summary>
    private static async Task SetKeyAsync(Guid id)
    {
        var show = AppServices.Engine.Show;
        var preset = show.PresetRoot.FindPreset(id);
        if (preset is null) return;
        var box = new KeyCaptureBox { Key = preset.Key, Width = 260 };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["LuxDangerBrush"], TextWrapping = TextWrapping.Wrap, Width = 320 };
        box.Changed += () => error.Text = box.Error ?? "";
        var panel = new StackPanel { Spacing = 10, Width = 340 };
        panel.Children.Add(new TextBlock { Text = "Pressing the key shows this preset, like clicking it. Ctrl + the key adds it on top of the current look.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = $"Key for \"{preset.Name}\"",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (box.Key is not null && KeyNames.UsedBy(show, box.Key, preset) is { } user)
            {
                error.Text = $"{KeyNames.Display(box.Key)} is already used by {user}.";
                args.Cancel = true;
            }
        };
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        AppServices.Document.Edit(ShowChange.Presets, _ => preset.Key = box.Key);
    }

    private async void UpdatePreset(Guid id)
    {
        var engine = AppServices.Engine;
        var preset = engine.Show.PresetRoot.FindPreset(id);
        if (preset is null) return;
        if (engine.Programmer.IsEmpty)
        {
            await DialogHelper.ShowMessageAsync("Nothing to save", "Set a look in the Control tab first.");
            return;
        }
        var fixtures = AppServices.Selection.Count > 0 ? AppServices.Selection.Ids : preset.FixtureIds.ToList();
        var recorded = engine.RecordPreset(preset.Name, fixtures);
        AppServices.Document.Edit(ShowChange.Presets, _ =>
        {
            preset.Values = recorded.Values;
            preset.Effects = recorded.Effects;
            preset.Color = GuessColor(preset) ?? preset.Color;
            if (preset.AutoName) preset.Name = PresetNamer.Suggest(engine.Show, preset);
        });
    }

    private static void DuplicatePreset(Guid id)
    {
        var root = AppServices.Engine.Show.PresetRoot;
        var preset = root.FindPreset(id);
        var parent = root.FindParentOf(id);
        if (preset is null || parent is null) return;
        var copy = new Preset
        {
            Name = preset.Name + " copy",
            Color = preset.Color,
            Values = preset.Values.ToList(),
            Effects = preset.Effects.Select(e => e.Clone()).ToList(),
        };
        AppServices.Document.Edit(ShowChange.Presets, _ => parent.Presets.Insert(parent.Presets.IndexOf(preset) + 1, copy));
    }

    // ------------------------------------------------------------------ drag and drop

    private void Tree_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
    {
        var node = args.Items.OfType<PresetNode>().FirstOrDefault();
        if (node is null) return;
        if (!node.IsFolder) args.Data.SetText(DragPrefix + node.Id);
        args.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
    }

    /// <summary>After the user rearranged the tree, copy the new order back into the show.</summary>
    private void Tree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        if (!Tree.CanReorderItems) return;
        AppServices.Document.Edit(ShowChange.Presets, show =>
        {
            var root = show.PresetRoot;
            var presets = root.AllPresets().ToDictionary(p => p.Id);
            var folders = root.AllFolders().ToDictionary(f => f.Id);
            Rebuild(root, Nodes, presets, folders);
        });
    }

    private static void Rebuild(PresetFolder folder, IEnumerable<PresetNode> nodes,
        Dictionary<Guid, Preset> presets, Dictionary<Guid, PresetFolder> folders)
    {
        folder.Folders = new List<PresetFolder>();
        folder.Presets = new List<Preset>();
        foreach (var node in nodes)
        {
            if (node.IsFolder && folders.TryGetValue(node.Id, out var sub))
            {
                folder.Folders.Add(sub);
                Rebuild(sub, node.Children, presets, folders);
            }
            else if (presets.TryGetValue(node.Id, out var preset))
            {
                folder.Presets.Add(preset);
                // A preset can't contain anything: anything dropped onto it moves next to it.
                if (node.Children.Count > 0)
                {
                    var holder = new PresetFolder();
                    Rebuild(holder, node.Children, presets, folders);
                    folder.Presets.AddRange(holder.Presets);
                    folder.Folders.AddRange(holder.Folders);
                }
            }
        }
    }
}
