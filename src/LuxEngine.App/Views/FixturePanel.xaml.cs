using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LuxEngine.App.Dialogs;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Shows;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LuxEngine.App.Views;

public sealed class FixtureRow : ObservableObject
{
    private string _name = "";
    private string _address = "";
    private Brush _colorBrush = new SolidColorBrush(Colors.Black);
    private Brush? _addressBrush;
    private Visibility _inPreset = Visibility.Collapsed;

    public FixtureRow(Guid id, string model)
    {
        Id = id;
        Model = model;
    }

    public Guid Id { get; }
    public string Model { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    public Brush ColorBrush { get => _colorBrush; set => SetProperty(ref _colorBrush, value); }
    public Brush? AddressBrush { get => _addressBrush; set => SetProperty(ref _addressBrush, value); }

    /// <summary>Visible when the light belongs to the preset being edited.</summary>
    public Visibility InPreset { get => _inPreset; set => SetProperty(ref _inPreset, value); }

    /// <summary>Used as the accessible name of the list item (screen readers, UI automation).</summary>
    public override string ToString() => Name;
}

public sealed partial class FixturePanel : UserControl
{
    private readonly DispatcherQueueTimer _colorTimer;
    private bool _syncingSelection;

    public ObservableCollection<FixtureRow> Rows { get; } = new();

    public FixturePanel()
    {
        InitializeComponent();
        AppServices.Document.Changed += change =>
        {
            if (change.HasFlag(ShowChange.Patch)) Refresh();
        };
        AppServices.Selection.Changed += SyncSelectionFromModel;
        AppServices.Selection.Changed += MarkActiveGroups;
        Loaded += (_, _) => Refresh();

        _colorTimer = DispatcherQueue.CreateTimer();
        _colorTimer.Interval = TimeSpan.FromMilliseconds(100);
        _colorTimer.Tick += (_, _) => UpdateColors();
        _colorTimer.Start();
    }

    public void Refresh()
    {
        var show = AppServices.Engine.Show;
        var conflicts = PatchHelper.FindConflicts(show);
        var conflicted = conflicts.SelectMany(c => new[] { c.First.Id, c.Second.Id }).ToHashSet();

        Rows.Clear();
        foreach (var fixture in show.Fixtures.OrderBy(f => f.Universe).ThenBy(f => f.Address))
        {
            var def = show.DefinitionOf(fixture);
            Rows.Add(new FixtureRow(fixture.Id, def is null ? "Unknown fixture type" : $"{def.DisplayName} · {fixture.ModeName}")
            {
                Name = fixture.Name,
                Address = $"{fixture.Universe}.{fixture.Address:000}",
                AddressBrush = conflicted.Contains(fixture.Id) ? Ui.Brush("LuxDangerBrush") : null,
            });
        }

        Header.Text = Rows.Count == 0 ? "FIXTURES" : $"FIXTURES ({Rows.Count})";
        EmptyState.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConflictBar.IsOpen = conflicts.Count > 0;
        ConflictBar.Message = conflicts.Count == 0 ? "" :
            string.Join("\n", conflicts.Take(3).Select(c => $"{c.First.Name} and {c.Second.Name} share DMX addresses.")) +
            "\nRight-click a fixture → Edit to change its address.";
        SyncSelectionFromModel();
        RefreshGroups();
    }

    // ------------------------------------------------------------------ groups

    private void RefreshGroups()
    {
        var show = AppServices.Engine.Show;
        GroupChips.Children.Clear();
        foreach (var group in show.Groups)
        {
            var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            text.Children.Add(new TextBlock { Text = group.Name, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = group.FixtureIds.Count.ToString(), Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
            if (group.Key is not null)
                text.Children.Add(new TextBlock { Text = KeyNames.Display(group.Key), FontFamily = new FontFamily("Consolas"), Foreground = Ui.Brush("LuxAccentBrush") });
            var chip = new Button { Content = text, Tag = group.Id, Padding = new Thickness(10, 3, 10, 4), CornerRadius = new CornerRadius(12) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, $"Group {group.Name}");
            ToolTipService.SetToolTip(chip, $"Select the {group.FixtureIds.Count} lights of \"{group.Name}\" (Ctrl+click adds them). Right-click for more.");
            chip.Click += (_, _) =>
            {
                bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                SelectGroup(group, add: ctrl);
            };
            chip.RightTapped += (_, e) =>
            {
                GroupMenu(group).ShowAt(chip, e.GetPosition(chip));
                e.Handled = true;
            };
            GroupChips.Children.Add(chip);
        }
        GroupChips.Visibility = show.Groups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        MarkActiveGroups();
    }

    /// <summary>A group whose lights are exactly the current selection is drawn in the accent colour.</summary>
    private void MarkActiveGroups()
    {
        var show = AppServices.Engine.Show;
        var selected = AppServices.Selection.Ids.ToHashSet();
        foreach (var chip in GroupChips.Children.OfType<Button>())
        {
            var group = show.Groups.FirstOrDefault(g => g.Id == (Guid)chip.Tag);
            bool active = group is not null && group.FixtureIds.Count > 0 && selected.SetEquals(group.FixtureIds);
            chip.Style = (Style)Application.Current.Resources[active ? "AccentButtonStyle" : "DefaultButtonStyle"];
        }
    }

    public void SelectGroup(Core.Fixtures.FixtureGroup group, bool add)
    {
        var show = AppServices.Engine.Show;
        var ids = group.FixtureIds.Where(id => show.FindFixture(id) is not null);
        AppServices.Selection.Set(add ? AppServices.Selection.Ids.Concat(ids).ToList() : ids);
    }

    /// <summary>Saves the selected lights as a group, in the order they were selected.</summary>
    public async Task NewGroupAsync()
    {
        var ids = AppServices.Selection.Ids.ToList();
        if (ids.Count == 0)
        {
            await DialogHelper.ShowMessageAsync("Select lights first", "Select the lights that belong together, then make the group. One click on the group selects them again.");
            return;
        }
        var show = AppServices.Engine.Show;
        var name = await DialogHelper.PromptAsync("New group", $"Name for these {ids.Count} lights", $"Group {show.Groups.Count + 1}", "Create");
        if (name is null) return;
        AppServices.Document.Edit(ShowChange.Patch, s => s.Groups.Add(new Core.Fixtures.FixtureGroup { Name = name, FixtureIds = ids }));
    }

    private async void NewGroup_Click(object sender, RoutedEventArgs e) => await NewGroupAsync();

    private MenuFlyout GroupMenu(Core.Fixtures.FixtureGroup group)
    {
        var menu = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = "Rename…", Icon = new FontIcon { Glyph = "\uE8AC" } };
        rename.Click += async (_, _) =>
        {
            var name = await DialogHelper.PromptAsync("Rename group", "Name", group.Name, "Rename");
            if (name is not null) AppServices.Document.Edit(ShowChange.Patch, _ => group.Name = name);
        };
        menu.Items.Add(rename);

        var update = new MenuFlyoutItem { Text = "Use the selected lights instead", Icon = new FontIcon { Glyph = "\uE72C" }, IsEnabled = AppServices.Selection.Count > 0 };
        update.Click += (_, _) =>
        {
            var ids = AppServices.Selection.Ids.ToList();
            AppServices.Document.Edit(ShowChange.Patch, _ => group.FixtureIds = ids);
        };
        menu.Items.Add(update);

        var key = new MenuFlyoutItem { Text = group.Key is null ? "Set a key…" : $"Change key ({KeyNames.Display(group.Key)})…", Icon = new FontIcon { Glyph = "\uE765" } };
        key.Click += async (_, _) => await SetGroupKeyAsync(group);
        menu.Items.Add(key);

        menu.Items.Add(new MenuFlyoutSeparator());
        var delete = new MenuFlyoutItem { Text = "Delete group", Icon = new FontIcon { Glyph = "\uE74D" } };
        delete.Click += (_, _) => AppServices.Document.Edit(ShowChange.Patch, s => s.Groups.Remove(group));
        menu.Items.Add(delete);
        return menu;
    }

    /// <summary>Gives a group its own key: pressing it selects the lights (Ctrl+key adds them).</summary>
    private static async Task SetGroupKeyAsync(Core.Fixtures.FixtureGroup group)
    {
        var show = AppServices.Engine.Show;
        var box = new KeyCaptureBox { Key = group.Key, Width = 260 };
        var error = new TextBlock { Foreground = Ui.Brush("LuxDangerBrush"), TextWrapping = TextWrapping.Wrap, Width = 320 };
        box.Changed += () => error.Text = box.Error ?? "";
        var panel = new StackPanel { Spacing = 10, Width = 340 };
        panel.Children.Add(new TextBlock { Text = "Pressing the key selects the lights of this group. Ctrl + the key adds them to the selection.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = $"Key for \"{group.Name}\"",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (box.Key is not null && KeyNames.UsedBy(show, box.Key, group) is { } user)
            {
                error.Text = $"{KeyNames.Display(box.Key)} is already used by {user}.";
                args.Cancel = true;
            }
        };
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        AppServices.Document.Edit(ShowChange.Patch, _ => group.Key = box.Key);
    }

    private void UpdateColors()
    {
        var inPreset = AppServices.PresetEditor.IsEditing ? AppServices.Engine.ProgrammerFixtures() : null;
        foreach (var row in Rows)
        {
            row.InPreset = inPreset?.Contains(row.Id) == true ? Visibility.Visible : Visibility.Collapsed;
            var color = ColorMixer.FromLook(AppServices.Engine.GetLook(row.Id));
            if (row.ColorBrush is SolidColorBrush b && b.Color == color) continue;
            row.ColorBrush = new SolidColorBrush(color);
        }
    }

    private void SyncSelectionFromModel()
    {
        _syncingSelection = true;
        try
        {
            foreach (var row in Rows)
            {
                bool selected = AppServices.Selection.Contains(row.Id);
                bool shown = FixtureList.SelectedItems.Contains(row);
                if (selected && !shown) FixtureList.SelectedItems.Add(row);
                else if (!selected && shown) FixtureList.SelectedItems.Remove(row);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        var keep = AppServices.Selection.Ids.Where(id => FixtureList.SelectedItems.OfType<FixtureRow>().Any(r => r.Id == id));
        var added = e.AddedItems.OfType<FixtureRow>().Select(r => r.Id);
        AppServices.Selection.Set(keep.Concat(added));
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) =>
        AppServices.Selection.Set(Rows.Select(r => r.Id));

    private async void Add_Click(object sender, RoutedEventArgs e) => await AddFixtureDialog.ShowAndAddAsync();

    private async void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FixtureRow row)
            await FixturePropertiesDialog.EditAsync(row.Id);
    }

    private void List_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not FixtureRow row) return;
        var show = AppServices.Engine.Show;
        var fixture = show.FindFixture(row.Id);
        if (fixture is null) return;

        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Edit name and address…", Icon = new FontIcon { Glyph = "" } };
        edit.Click += async (_, _) => await FixturePropertiesDialog.EditAsync(row.Id);
        menu.Items.Add(edit);

        var editType = new MenuFlyoutItem { Text = "Edit fixture type (channels, ranges)…", Icon = new FontIcon { Glyph = "\uE90F" } };
        editType.Click += async (_, _) => await FixtureEditorDialog.EditAsync(row.Id);
        menu.Items.Add(editType);

        if (AppServices.PresetEditor.IsEditing && AppServices.Engine.ProgrammerFixtures().Contains(row.Id))
        {
            var removeFromPreset = new MenuFlyoutItem { Text = "Remove from the preset being edited", Icon = new FontIcon { Glyph = "\uE738" } };
            removeFromPreset.Click += (_, _) => AppServices.Engine.ReleaseFixture(row.Id);
            menu.Items.Add(removeFromPreset);
        }

        var chosen = AppServices.Selection.Contains(row.Id) && AppServices.Selection.Count > 1
            ? AppServices.Selection.Ids.Select(show.FindFixture).OfType<Core.Fixtures.FixtureInstance>().ToList()
            : new List<Core.Fixtures.FixtureInstance> { fixture };
        bool HasChannel(Core.Fixtures.FixtureInstance f, Core.Fixtures.ChannelType type) =>
            show.DefinitionOf(f)?.Channels.Any(c => c.Type == type) == true;
        var panTargets = chosen.Where(f => HasChannel(f, Core.Fixtures.ChannelType.Pan)).ToList();
        var tiltTargets = chosen.Where(f => HasChannel(f, Core.Fixtures.ChannelType.Tilt)).ToList();
        if (panTargets.Count + tiltTargets.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            string suffix = chosen.Count > 1 ? $" ({chosen.Count} lights)" : "";
            if (panTargets.Count > 0)
            {
                var invertPan = new ToggleMenuFlyoutItem { Text = "Invert pan" + suffix, IsChecked = panTargets.All(f => f.InvertPan) };
                invertPan.Click += (_, _) => AppServices.Document.Edit(ShowChange.None, _ =>
                {
                    foreach (var f in panTargets) f.InvertPan = invertPan.IsChecked;
                });
                menu.Items.Add(invertPan);
            }
            if (tiltTargets.Count > 0)
            {
                var invertTilt = new ToggleMenuFlyoutItem { Text = "Invert tilt" + suffix, IsChecked = tiltTargets.All(f => f.InvertTilt) };
                invertTilt.Click += (_, _) => AppServices.Document.Edit(ShowChange.None, _ =>
                {
                    foreach (var f in tiltTargets) f.InvertTilt = invertTilt.IsChecked;
                });
                menu.Items.Add(invertTilt);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        var sameType = new MenuFlyoutItem { Text = "Select all of this type", Icon = new FontIcon { Glyph = "" } };
        sameType.Click += (_, _) => AppServices.Selection.Set(show.Fixtures.Where(f => f.DefinitionId == fixture.DefinitionId).Select(f => f.Id));
        menu.Items.Add(sameType);

        menu.Items.Add(new MenuFlyoutSeparator());
        var delete = new MenuFlyoutItem { Text = "Remove from show", Icon = new FontIcon { Glyph = "" } };
        delete.Click += async (_, _) =>
        {
            var targets = AppServices.Selection.Contains(row.Id) && AppServices.Selection.Count > 1
                ? AppServices.Selection.Ids.ToList()
                : new List<Guid> { row.Id };
            var text = targets.Count == 1 ? $"Remove \"{row.Name}\"?" : $"Remove {targets.Count} selected fixtures?";
            if (!await DialogHelper.ConfirmAsync("Remove fixtures", text + " Their values are also removed from presets.", "Remove")) return;
            lock (AppServices.Engine.SyncRoot)
            {
                foreach (var id in targets) AppServices.Engine.Programmer.ReleaseFixture(id);
            }
            AppServices.Document.Edit(ShowChange.Patch | ShowChange.Presets, s =>
            {
                foreach (var id in targets) s.RemoveFixture(id);
            });
        };
        menu.Items.Add(delete);
        menu.ShowAt((FrameworkElement)e.OriginalSource, e.GetPosition((FrameworkElement)e.OriginalSource));
        e.Handled = true;
    }
}
