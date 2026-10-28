using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Cues;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace LuxEngine.App.Views;

/// <summary>
/// A grid of cue buttons that switch presets on and off on top of the cue list.
/// Buttons that touch each other form a group where only one can be on.
/// </summary>
public sealed class ButtonPageView : UserControl
{
    private const string DragPrefix = "lux-button:";

    private static readonly string[] Palette =
    {
        "#3B82F6", "#EF4444", "#22C55E", "#F59E0B", "#A855F7", "#EC4899", "#14B8A6", "#F97316", "#94A3B8",
    };

    private readonly Grid _grid = new();
    private readonly Dictionary<Guid, (Border Face, Rectangle Level)> _faces = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

    public ButtonPageView()
    {
        var release = new Button { Content = "Release all buttons" };
        release.Click += (_, _) => AppServices.Engine.ReleaseButtons();
        var hint = new TextBlock
        {
            Text = "Click a button to switch it on or off. Buttons that touch each other are linked: only one of them can be on. "
                 + "Drag presets onto a button or an empty square; drag a button to move it.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        var toolbar = new Grid { ColumnSpacing = 12 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(hint, 1);
        toolbar.Children.Add(release);
        toolbar.Children.Add(hint);

        var root = new Grid { RowSpacing = 8, Padding = new Thickness(12, 4, 12, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_grid, 1);
        root.Children.Add(toolbar);
        root.Children.Add(_grid);
        Content = root;

        AppServices.Document.Changed += change =>
        {
            if (change.HasFlag(ShowChange.Cues) || change.HasFlag(ShowChange.Presets)) Rebuild();
        };
        Loaded += (_, _) => Rebuild();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += (_, _) => UpdateLevels();
        _timer.Start();
    }

    private static ButtonPage Page => AppServices.Engine.Show.ButtonPage;

    // ------------------------------------------------------------------ layout

    public void Rebuild()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _faces.Clear();
        var page = Page;
        for (int r = 0; r < page.Rows; r++) _grid.RowDefinitions.Add(new RowDefinition());
        for (int c = 0; c < page.Columns; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition());

        // Connectors first so they sit behind the buttons and show which buttons are linked.
        foreach (var b in page.Buttons)
        {
            if (page.At(b.Row, b.Column + 1) is not null) AddConnector(b.Row, b.Column, horizontal: true);
            if (page.At(b.Row + 1, b.Column) is not null) AddConnector(b.Row, b.Column, horizontal: false);
        }

        for (int r = 0; r < page.Rows; r++)
        for (int c = 0; c < page.Columns; c++)
        {
            var button = page.At(r, c);
            var cell = button is null ? EmptyCell(r, c) : ButtonCell(button);
            Grid.SetRow(cell, r);
            Grid.SetColumn(cell, c);
            _grid.Children.Add(cell);
        }
        UpdateLevels();
    }

    private void AddConnector(int row, int column, bool horizontal)
    {
        var line = new Rectangle
        {
            Fill = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            Width = horizontal ? double.NaN : 6,
            Height = horizontal ? 6 : double.NaN,
            HorizontalAlignment = horizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Stretch,
            Margin = horizontal ? new Thickness(40, 0, 40, 0) : new Thickness(0, 24, 0, 24),
        };
        Grid.SetRow(line, row);
        Grid.SetColumn(line, column);
        if (horizontal) Grid.SetColumnSpan(line, 2);
        else Grid.SetRowSpan(line, 2);
        _grid.Children.Add(line);
    }

    private FrameworkElement EmptyCell(int row, int column)
    {
        var cell = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(4),
            MinHeight = 56,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Content = new FontIcon { Glyph = "", FontSize = 14, Opacity = 0.35 },
            AllowDrop = true,
        };
        ToolTipService.SetToolTip(cell, "Add a cue button here");
        cell.Click += async (_, _) =>
        {
            var button = Create(row, column, AppServices.SelectedPresetId);
            await EditAsync(button);
        };
        cell.DragOver += DragOverCell;
        cell.Drop += async (_, e) =>
        {
            var (preset, moved) = await DroppedAsync(e);
            if (moved is Guid id) Move(id, row, column);
            else if (preset is Guid p) Create(row, column, p);
        };
        return cell;
    }

    private CueButton Create(int row, int column, Guid? presetId)
    {
        var button = new CueButton
        {
            Row = row,
            Column = column,
            Name = $"Button {Page.Buttons.Count + 1}",
            Color = Palette[Page.Buttons.Count % Palette.Length],
        };
        var preset = presetId is Guid id ? AppServices.Engine.Show.PresetRoot.FindPreset(id) : null;
        if (preset is not null)
        {
            button.PresetIds.Add(preset.Id);
            button.Name = preset.Name;
            if (!string.IsNullOrWhiteSpace(preset.Color)) button.Color = preset.Color;
        }
        AppServices.Document.Edit(ShowChange.Cues, show => show.ButtonPage.Buttons.Add(button));
        return button;
    }

    private FrameworkElement ButtonCell(CueButton button)
    {
        var color = ColorMixer.Parse(button.Color, Colors.SteelBlue);
        var root = AppServices.Engine.Show.PresetRoot;
        var names = button.PresetIds.Select(root.FindPreset).OfType<Core.Presets.Preset>().Select(p => p.Name).ToList();

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(button.Name) ? "Button" : button.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var detail = new TextBlock
        {
            Text = (button.Mode == CueButtonMode.Flash ? "Flash · " : "") + (names.Count == 0 ? "No presets" : string.Join(" + ", names)),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.8,
        };
        var level = new Rectangle
        {
            Height = 4, RadiusX = 2, RadiusY = 2, Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            Fill = new SolidColorBrush(Colors.White),
        };
        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(title);
        stack.Children.Add(detail);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition());
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(level, 1);
        content.Children.Add(stack);
        content.Children.Add(level);

        var face = new Border
        {
            Margin = new Thickness(4),
            MinHeight = 56,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(color),
            Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
            Child = content,
            AllowDrop = true,
            CanDrag = true,
            Tag = color,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(face, title.Text + " button");
        ToolTipService.SetToolTip(face, $"{title.Text}\n{detail.Text}\nRight-click to edit");

        face.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(face).Properties.IsLeftButtonPressed) return;
            if (button.Mode == CueButtonMode.Flash)
            {
                AppServices.Engine.SetButton(button.Id, true);
                face.CapturePointer(e.Pointer);
            }
        };
        void FlashOff(object s, PointerRoutedEventArgs e)
        {
            if (button.Mode == CueButtonMode.Flash) AppServices.Engine.SetButton(button.Id, false);
        }
        face.PointerReleased += FlashOff;
        face.PointerCaptureLost += FlashOff;
        face.Tapped += (_, _) =>
        {
            if (button.Mode == CueButtonMode.Toggle) AppServices.Engine.ToggleButton(button.Id);
        };

        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Edit…", Icon = new FontIcon { Glyph = "" } };
        edit.Click += async (_, _) => await EditAsync(button);
        var add = new MenuFlyoutItem { Text = "Add the selected preset", Icon = new FontIcon { Glyph = "" } };
        add.Click += (_, _) => { if (AppServices.SelectedPresetId is Guid p) AddPreset(button, p); };
        var remove = new MenuFlyoutItem { Text = "Remove button", Icon = new FontIcon { Glyph = "" } };
        remove.Click += async (_, _) =>
        {
            if (!await DialogHelper.ConfirmAsync("Remove button?", $"Remove \"{title.Text}\"? The presets themselves are kept.")) return;
            AppServices.Engine.SetButton(button.Id, false);
            AppServices.Document.Edit(ShowChange.Cues, show => show.ButtonPage.Buttons.Remove(button));
        };
        menu.Opening += (_, _) => add.IsEnabled = AppServices.SelectedPresetId is not null;
        menu.Items.Add(edit);
        menu.Items.Add(add);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);
        face.ContextFlyout = menu;

        face.DragStarting += (_, e) => e.Data.SetText(DragPrefix + button.Id);
        face.DragOver += DragOverCell;
        face.Drop += async (_, e) =>
        {
            var (preset, moved) = await DroppedAsync(e);
            if (moved is Guid id && id != button.Id) Move(id, button.Row, button.Column);
            else if (preset is Guid p) AddPreset(button, p);
        };

        _faces[button.Id] = (face, level);
        return face;
    }

    /// <summary>Shows on/off state and the fade level of every button.</summary>
    private void UpdateLevels()
    {
        foreach (var (id, (face, bar)) in _faces)
        {
            double level = AppServices.Engine.ButtonLevel(id);
            bool on = AppServices.Engine.IsButtonOn(id);
            var c = (Color)face.Tag;
            byte alpha = (byte)(40 + 200 * level);
            if (face.Background is SolidColorBrush brush && brush.Color.A != alpha)
                brush.Color = Color.FromArgb(alpha, c.R, c.G, c.B);
            bar.Width = Math.Max(0, (face.ActualWidth - 24) * level);
            bar.Opacity = on ? 1 : 0.5;
        }
    }

    // ------------------------------------------------------------------ editing

    private static void AddPreset(CueButton button, Guid presetId)
    {
        if (button.PresetIds.Contains(presetId) || AppServices.Engine.Show.PresetRoot.FindPreset(presetId) is null) return;
        AppServices.Document.Edit(ShowChange.Cues, _ => button.PresetIds.Add(presetId));
    }

    /// <summary>Moves a button to a cell; if another button is there they swap places.</summary>
    private static void Move(Guid id, int row, int column)
    {
        AppServices.Document.Edit(ShowChange.Cues, show =>
        {
            var page = show.ButtonPage;
            var button = page.Find(id);
            if (button is null) return;
            var other = page.At(row, column);
            if (other is not null)
            {
                other.Row = button.Row;
                other.Column = button.Column;
            }
            button.Row = row;
            button.Column = column;
        });
    }

    private async Task EditAsync(CueButton button)
    {
        var show = AppServices.Engine.Show;
        var name = new TextBox { Header = "Name", Text = button.Name };
        var mode = new ComboBox { Header = "How it works", HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add("Toggle: click on, click off");
        mode.Items.Add("Flash: on only while held");
        mode.SelectedIndex = (int)button.Mode;
        var fadeIn = new NumberBox { Header = "Fade in (s)", Minimum = 0, Maximum = 600, SmallChange = 0.5, Value = button.FadeIn, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var fadeOut = new NumberBox { Header = "Fade out (s)", Minimum = 0, Maximum = 600, SmallChange = 0.5, Value = button.FadeOut, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

        string color = button.Color;
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var hex in Palette)
        {
            var swatch = new Button
            {
                Width = 28, Height = 28, Padding = new Thickness(0), CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(ColorMixer.Parse(hex, Colors.Gray)),
                BorderThickness = new Thickness(hex.Equals(color, StringComparison.OrdinalIgnoreCase) ? 3 : 0),
                BorderBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            };
            swatch.Click += (_, _) =>
            {
                color = hex;
                foreach (var s in swatches.Children.OfType<Button>()) s.BorderThickness = new Thickness(s == swatch ? 3 : 0);
            };
            swatches.Children.Add(swatch);
        }

        var presetIds = button.PresetIds.ToList();
        var presetList = new StackPanel { Spacing = 4 };
        void ShowPresets()
        {
            presetList.Children.Clear();
            foreach (var id in presetIds.ToList())
            {
                var preset = show.PresetRoot.FindPreset(id);
                if (preset is null) continue;
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(ColorMixer.Parse(preset.Color, Colors.LightGray)), VerticalAlignment = VerticalAlignment.Center };
                var label = new TextBlock { Text = preset.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 11 }, Padding = new Thickness(6, 4, 6, 4) };
                remove.Click += (_, _) => { presetIds.Remove(id); ShowPresets(); };
                Grid.SetColumn(label, 1);
                Grid.SetColumn(remove, 2);
                row.Children.Add(dot);
                row.Children.Add(label);
                row.Children.Add(remove);
                presetList.Children.Add(row);
            }
            if (presetList.Children.Count == 0)
                presetList.Children.Add(new TextBlock { Text = "No presets yet.", Opacity = 0.6 });
        }
        ShowPresets();

        var selected = AppServices.SelectedPresetId is Guid sid ? show.PresetRoot.FindPreset(sid) : null;
        var addSelected = new Button
        {
            Content = selected is null ? "Click a preset on the left to add it" : $"+ Add \"{selected.Name}\"",
            IsEnabled = selected is not null,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        addSelected.Click += (_, _) =>
        {
            if (selected is not null && !presetIds.Contains(selected.Id)) presetIds.Add(selected.Id);
            ShowPresets();
        };

        var fades = new Grid { ColumnSpacing = 8 };
        fades.ColumnDefinitions.Add(new ColumnDefinition());
        fades.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(fadeOut, 1);
        fades.Children.Add(fadeIn);
        fades.Children.Add(fadeOut);

        var layout = new StackPanel { Spacing = 12, Width = 420 };
        layout.Children.Add(name);
        layout.Children.Add(mode);
        layout.Children.Add(fades);
        layout.Children.Add(new TextBlock { Text = "Color", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        layout.Children.Add(swatches);
        layout.Children.Add(new TextBlock { Text = "Presets on this button", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        layout.Children.Add(new ScrollViewer { Content = presetList, MaxHeight = 200 });
        layout.Children.Add(addSelected);

        var dialog = new ContentDialog
        {
            Title = "Cue button",
            Content = layout,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;

        static double Safe(double v) => double.IsNaN(v) ? 0 : v;
        var newMode = mode.SelectedIndex == 1 ? CueButtonMode.Flash : CueButtonMode.Toggle;
        if (newMode != button.Mode) AppServices.Engine.SetButton(button.Id, false);
        AppServices.Document.Edit(ShowChange.Cues, _ =>
        {
            button.Name = name.Text.Trim();
            button.Mode = newMode;
            button.FadeIn = Safe(fadeIn.Value);
            button.FadeOut = Safe(fadeOut.Value);
            button.Color = color;
            button.PresetIds = presetIds;
        });
    }

    // ------------------------------------------------------------------ drag and drop

    private static void DragOverCell(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        e.AcceptedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
    }

    /// <summary>Returns the dropped preset id, or the id of a button being moved.</summary>
    private static async Task<(Guid? Preset, Guid? Button)> DroppedAsync(DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return (null, null);
        var text = await e.DataView.GetTextAsync();
        if (text.StartsWith(DragPrefix) && Guid.TryParse(text[DragPrefix.Length..], out var button)) return (null, button);
        if (text.StartsWith(PresetPanel.DragPrefix) && Guid.TryParse(text[PresetPanel.DragPrefix.Length..], out var preset)) return (preset, null);
        return (null, null);
    }
}
