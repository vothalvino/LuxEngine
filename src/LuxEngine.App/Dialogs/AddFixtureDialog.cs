using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Shows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuxEngine.App.Dialogs;

/// <summary>Search the fixture library and patch one or more lights.</summary>
public sealed class AddFixtureDialog : ContentDialog
{
    private const int MaxResults = 300;

    private readonly TextBox _search = new() { PlaceholderText = "Search by brand or model, e.g. \"chauvet par\"" };
    private readonly ComboBox _category = new() { PlaceholderText = "All types", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListView _results = new() { SelectionMode = ListViewSelectionMode.Single, Height = 380 };
    private readonly TextBlock _resultCount = new() { Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
    private readonly TextBlock _title = new() { Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _channels = new() { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
    private readonly ComboBox _mode = new() { Header = "Mode (DMX personality)", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumberBox _quantity = new() { Header = "How many", Value = 1, Minimum = 1, Maximum = 512, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly NumberBox _universe = new() { Header = "Universe", Value = 1, Minimum = 1, Maximum = 64000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly NumberBox _address = new() { Header = "Start address", Minimum = 1, Maximum = 512, PlaceholderText = "Auto", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly TextBox _name = new() { Header = "Name" };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private bool _createOwn;

    private AddFixtureDialog()
    {
        Title = "Add fixtures";
        PrimaryButtonText = "Add to show";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;

        _category.Items.Add("All types");
        foreach (var cat in AppServices.Library.All.Select(d => d.Category).Distinct().OrderBy(c => c)) _category.Items.Add(cat);
        _category.SelectedIndex = 0;

        _search.TextChanged += (_, _) => RunSearch();
        _category.SelectionChanged += (_, _) => RunSearch();
        _results.SelectionChanged += (_, _) => ShowDetails();
        _mode.SelectionChanged += (_, _) => UpdateSummary();
        _quantity.ValueChanged += (_, _) => UpdateSummary();
        _universe.ValueChanged += (_, _) => UpdateSummary();
        _address.ValueChanged += (_, _) => UpdateSummary();

        var createLink = new HyperlinkButton { Content = "Can't find your light? Create it yourself…", Padding = new Thickness(0) };
        createLink.Click += (_, _) =>
        {
            _createOwn = true;
            Hide();
        };

        var left = new StackPanel { Spacing = 8, Width = 430 };
        left.Children.Add(_search);
        left.Children.Add(_category);
        left.Children.Add(_results);
        left.Children.Add(_resultCount);
        left.Children.Add(createLink);

        var numbers = new Grid { ColumnSpacing = 8 };
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_universe, 1);
        Grid.SetColumn(_address, 2);
        numbers.Children.Add(_quantity);
        numbers.Children.Add(_universe);
        numbers.Children.Add(_address);

        var right = new StackPanel { Spacing = 12, Width = 360 };
        right.Children.Add(_title);
        right.Children.Add(_channels);
        right.Children.Add(_mode);
        right.Children.Add(numbers);
        right.Children.Add(_name);
        right.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Child = _summary,
        });

        var layout = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        layout.Children.Add(left);
        layout.Children.Add(right);
        Content = layout;

        RunSearch();
        Opened += (_, _) => _search.Focus(FocusState.Programmatic);
    }

    private FixtureDefinition? Selected => (_results.SelectedItem as ListViewItem)?.Tag as FixtureDefinition;

    private void RunSearch()
    {
        string? category = _category.SelectedIndex > 0 ? _category.SelectedItem as string : null;
        var matches = AppServices.Library.Search(_search.Text, category).ToList();
        _results.Items.Clear();
        foreach (var def in matches.Take(MaxResults))
        {
            var row = new StackPanel { Padding = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = def.Model, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(new TextBlock
            {
                Text = $"{def.Manufacturer} · {def.Category}{(def.IsUserDefined ? " · made by you" : "")}",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = Ui.Brush("TextFillColorSecondaryBrush"),
            });
            _results.Items.Add(new ListViewItem { Content = row, Tag = def });
        }
        _resultCount.Text = matches.Count > MaxResults
            ? $"Showing {MaxResults} of {matches.Count} fixtures. Type more to narrow it down."
            : $"{matches.Count} fixture(s) found";
        if (_results.Items.Count > 0) _results.SelectedIndex = 0;
        else ShowDetails();
    }

    private void ShowDetails()
    {
        var def = Selected;
        IsPrimaryButtonEnabled = def is not null;
        _mode.Items.Clear();
        if (def is null)
        {
            _title.Text = "No fixture selected";
            _channels.Text = "";
            _summary.Text = "";
            return;
        }
        _title.Text = def.DisplayName;
        foreach (var mode in def.Modes) _mode.Items.Add($"{mode.Name} ({mode.ChannelCount} ch)");
        _mode.SelectedIndex = def.Modes.Count > 0 ? 0 : -1;
        _name.Text = def.Model;
    }

    private FixtureMode? SelectedMode =>
        Selected is { } def && _mode.SelectedIndex >= 0 && _mode.SelectedIndex < def.Modes.Count ? def.Modes[_mode.SelectedIndex] : null;

    private void UpdateSummary()
    {
        var def = Selected;
        var mode = SelectedMode;
        if (def is null || mode is null) return;

        _channels.Text = string.Join(" · ", mode.Channels.Select((c, i) => $"{i + 1}: {c ?? "(unused)"}"));
        int count = (int)Math.Max(1, double.IsNaN(_quantity.Value) ? 1 : _quantity.Value);
        int universe = (int)Math.Max(1, double.IsNaN(_universe.Value) ? 1 : _universe.Value);
        int? start = double.IsNaN(_address.Value)
            ? PatchHelper.NextFreeAddress(AppServices.Engine.Show, universe, mode.ChannelCount)
            : (int)_address.Value;

        if (start is null)
        {
            _summary.Text = $"Universe {universe} is full. They will be placed in the next universe with free space.";
            return;
        }
        int total = count * mode.ChannelCount;
        int end = start.Value + total - 1;
        _summary.Text = end <= 512
            ? $"{count} × {mode.ChannelCount} channels = {total} channels\nUniverse {universe}, addresses {start}–{end}" +
              (count > 1 ? $"\nSet your lights to {string.Join(", ", Enumerable.Range(0, Math.Min(count, 6)).Select(i => start + i * mode.ChannelCount))}{(count > 6 ? "…" : "")}" : "")
            : $"{count} × {mode.ChannelCount} channels = {total} channels\nStarts at {universe}.{start}; lights that don't fit continue in the next universe.";
    }

    /// <summary>Shows the dialog and patches the chosen fixtures.</summary>
    public static async Task ShowAndAddAsync()
    {
        var dialog = new AddFixtureDialog();
        var result = await DialogHelper.ShowAsync(dialog);

        if (dialog._createOwn)
        {
            await FixtureEditorDialog.CreateAsync();
            return;
        }
        if (result != ContentDialogResult.Primary || dialog.Selected is not { } def || dialog.SelectedMode is not { } mode) return;

        int count = (int)Math.Max(1, double.IsNaN(dialog._quantity.Value) ? 1 : dialog._quantity.Value);
        int universe = (int)Math.Max(1, double.IsNaN(dialog._universe.Value) ? 1 : dialog._universe.Value);
        int? address = double.IsNaN(dialog._address.Value) ? null : (int)dialog._address.Value;
        string name = string.IsNullOrWhiteSpace(dialog._name.Text) ? def.Model : dialog._name.Text.Trim();

        IReadOnlyList<FixtureInstance> added = Array.Empty<FixtureInstance>();
        AppServices.Document.Edit(ShowChange.Patch, show =>
            added = PatchHelper.AddFixtures(show, def, mode.Name, count, universe, address, name));
        AppServices.Selection.Set(added.Select(f => f.Id));
    }
}
