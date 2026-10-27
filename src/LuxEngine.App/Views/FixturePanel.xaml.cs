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
        Loaded += (_, _) => Refresh();

        _colorTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
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
                AddressBrush = conflicted.Contains(fixture.Id) ? (Brush)Application.Current.Resources["LuxDangerBrush"] : null,
            });
        }

        Header.Text = Rows.Count == 0 ? "FIXTURES" : $"FIXTURES ({Rows.Count})";
        EmptyState.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConflictBar.IsOpen = conflicts.Count > 0;
        ConflictBar.Message = conflicts.Count == 0 ? "" :
            string.Join("\n", conflicts.Take(3).Select(c => $"{c.First.Name} and {c.Second.Name} share DMX addresses.")) +
            "\nRight-click a fixture → Edit to change its address.";
        SyncSelectionFromModel();
    }

    private void UpdateColors()
    {
        foreach (var row in Rows)
        {
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
