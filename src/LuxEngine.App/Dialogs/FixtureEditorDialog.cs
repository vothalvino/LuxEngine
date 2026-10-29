using System.Text.RegularExpressions;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Fixtures;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LuxEngine.App.Dialogs;

/// <summary>Create or edit a fixture type by hand: name it and list its channels in DMX order.</summary>
public sealed class FixtureEditorDialog : ContentDialog
{
    private static readonly string[] Categories =
    {
        "Color Changer", "Dimmer", "Moving Head", "Scanner", "Strobe", "Laser", "Smoke", "Effect", "Blinder", "Pixel Bar", "Other",
    };

    private readonly TextBox _manufacturer = new() { Header = "Brand", PlaceholderText = "e.g. Stairville" };
    private readonly TextBox _model = new() { Header = "Model", PlaceholderText = "e.g. LED Par 56" };
    private readonly ComboBox _category = new() { Header = "Type of light", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _modeName = new() { Header = "Mode name", PlaceholderText = "e.g. 7-channel" };
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly CheckBox _saveToLibrary = new() { Content = "Also update it in my fixture library, for future shows", IsChecked = true };
    private readonly FixtureDefinition? _existing;
    private readonly string? _existingMode;
    private readonly TextBlock _error = new() { Foreground = Ui.Brush("LuxDangerBrush"), TextWrapping = TextWrapping.Wrap };

    private FixtureEditorDialog(FixtureDefinition? existing = null, string? modeName = null)
    {
        _existing = existing;
        _existingMode = modeName;
        Title = existing is null ? "Create a fixture type" : "Edit fixture type";
        PrimaryButtonText = existing is null ? "Save fixture" : "Save changes";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        foreach (var c in Categories) _category.Items.Add(c);
        _category.SelectedIndex = 0;

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_model, 1);
        Grid.SetColumn(_category, 2);
        header.Children.Add(_manufacturer);
        header.Children.Add(_model);
        header.Children.Add(_category);

        var addButton = new Button { Content = "+ Add channel" };
        addButton.Click += (_, _) => AddRow(ChannelType.Generic);

        var templates = new DropDownButton { Content = "Start from a template" };
        var menu = new MenuFlyout();
        void Template(string text, params ChannelType[] channels)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) =>
            {
                _rows.Children.Clear();
                _channelRows.Clear();
                foreach (var t in channels) AddRow(t);
            };
            menu.Items.Add(item);
        }
        Template("RGB par (3 ch)", ChannelType.Red, ChannelType.Green, ChannelType.Blue);
        Template("Dimmer + RGB (4 ch)", ChannelType.Intensity, ChannelType.Red, ChannelType.Green, ChannelType.Blue);
        Template("RGBW + dimmer + strobe (6 ch)", ChannelType.Intensity, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Strobe);
        Template("Dimmer + RGBWA UV + strobe (8 ch)", ChannelType.Intensity, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV, ChannelType.Strobe);
        Template("Simple moving head (8 ch)", ChannelType.Pan, ChannelType.Tilt, ChannelType.Speed, ChannelType.Intensity, ChannelType.Strobe, ChannelType.ColorWheel, ChannelType.Gobo, ChannelType.Effect);
        templates.Flyout = menu;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(addButton);
        buttons.Children.Add(templates);

        var layout = new StackPanel { Spacing = 12, Width = 720 };
        layout.Children.Add(header);
        layout.Children.Add(_modeName);
        layout.Children.Add(new TextBlock
        {
            Text = "Channels in DMX order (channel 1 first). Check the manual of your light for this list.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        var columns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Opacity = 0.7 };
        foreach (var (text, width) in new[] { ("#", 28.0), ("Type", 170.0), ("Name", 190.0), ("Default", 72.0), ("Value ranges", 120.0) })
            columns.Children.Add(new TextBlock { Text = text, Width = width });
        layout.Children.Add(columns);
        layout.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 400 });
        layout.Children.Add(buttons);
        if (existing is not null) layout.Children.Add(_saveToLibrary);
        layout.Children.Add(_error);
        Content = layout;

        if (existing is null)
        {
            AddRow(ChannelType.Intensity);
            AddRow(ChannelType.Red);
            AddRow(ChannelType.Green);
            AddRow(ChannelType.Blue);
        }
        else
        {
            _manufacturer.Text = existing.Manufacturer;
            _model.Text = existing.Model;
            if (!_category.Items.Contains(existing.Category)) _category.Items.Add(existing.Category);
            _category.SelectedItem = existing.Category;
            var mode = existing.Modes.FirstOrDefault(m => m.Name == modeName);
            _modeName.Text = mode?.Name ?? "";
            if (existing.Modes.Count > 1)
                layout.Children.Insert(2, new InfoBar
                {
                    IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational,
                    Message = $"This light has {existing.Modes.Count} modes. You are editing the \"{mode?.Name}\" mode used by your fixture; the other modes stay as they are.",
                });
            foreach (var name in mode?.Channels ?? new())
            {
                var channel = existing.Channels.FirstOrDefault(c => c.Name == name);
                if (channel is not null) AddRow(channel.Type, channel);
            }
        }

        PrimaryButtonClick += (_, args) =>
        {
            var problem = Validate();
            _error.Text = problem ?? "";
            args.Cancel = problem is not null;
        };
    }

    /// <summary>The controls for one channel: its type, name, default value and optional value ranges.</summary>
    private sealed class ChannelRow
    {
        /// <summary>The channel this row was loaded from when editing, or null for a new channel.</summary>
        public string? OriginalName { get; init; }
        public StackPanel Root { get; init; } = null!;
        public TextBlock Number { get; init; } = null!;
        public ComboBox TypeBox { get; init; } = null!;
        public TextBox NameBox { get; init; } = null!;
        public NumberBox DefaultBox { get; init; } = null!;
        public StackPanel Ranges { get; init; } = null!;
        public ToggleButton RangesToggle { get; init; } = null!;

        public ChannelType Type => (TypeBox.SelectedItem as ComboBoxItem)?.Tag is ChannelType t ? t : ChannelType.Generic;
        public string Name => NameBox.Text.Trim().Length == 0 ? Pretty(Type) : NameBox.Text.Trim();

        public List<ChannelCapability> Capabilities() =>
            Ranges.Children.OfType<Grid>().Select(g => new ChannelCapability
            {
                From = ToByte(((NumberBox)g.Children[0]).Value),
                To = ToByte(((NumberBox)g.Children[1]).Value),
                Label = ((TextBox)g.Children[2]).Text.Trim(),
            }).ToList();
    }

    private readonly List<ChannelRow> _channelRows = new();

    private static int ToByte(double value) => double.IsNaN(value) ? 0 : (int)Math.Round(value);

    private static NumberBox ByteBox(double value, string? header = null, string? tip = null)
    {
        var box = new NumberBox
        {
            Minimum = 0, Maximum = 255, Value = value, Width = 72, Header = header,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        if (tip is not null) ToolTipService.SetToolTip(box, tip);
        return box;
    }

    private void AddRow(ChannelType type, ChannelDefinition? existing = null)
    {
        var number = new TextBlock { Width = 28, VerticalAlignment = VerticalAlignment.Center };
        var typeBox = new ComboBox { Width = 170 };
        foreach (var t in Enum.GetValues<ChannelType>()) typeBox.Items.Add(new ComboBoxItem { Content = Pretty(t), Tag = t });
        typeBox.SelectedIndex = (int)type;
        var name = new TextBox { Width = 190, PlaceholderText = "Channel name", Text = existing?.Name ?? Pretty(type) };
        var defaultBox = ByteBox(existing?.Default ?? (type is ChannelType.Pan or ChannelType.Tilt ? 128 : 0), tip: "Default value (used when nothing controls this channel)");
        typeBox.SelectionChanged += (_, _) =>
        {
            // Keep the name in sync until the user types their own.
            if (typeBox.SelectedItem is ComboBoxItem { Tag: ChannelType t } && (name.Text.Length == 0 || Enum.GetValues<ChannelType>().Any(x => Pretty(x) == name.Text)))
                name.Text = Pretty(t);
        };
        var rangesToggle = new ToggleButton { Content = "Ranges" };
        ToolTipService.SetToolTip(rangesToggle, "Describe what each DMX value range does, e.g. 0–9 Off, 10–255 Strobe slow → fast");
        var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 13 }, Padding = new Thickness(8, 6, 8, 6) };
        ToolTipService.SetToolTip(remove, "Remove channel");

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(number);
        header.Children.Add(typeBox);
        header.Children.Add(name);
        header.Children.Add(defaultBox);
        header.Children.Add(rangesToggle);
        header.Children.Add(remove);

        var ranges = new StackPanel { Spacing = 4 };
        var addRange = new Button { Content = "+ Add range" };
        var rangesPanel = new StackPanel { Spacing = 6, Margin = new Thickness(36, 2, 0, 8), Visibility = Visibility.Collapsed };
        rangesPanel.Children.Add(new TextBlock
        {
            Text = "Value ranges from the light's manual. They appear in the Control tab so you can pick a setting by name.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        });
        rangesPanel.Children.Add(ranges);
        rangesPanel.Children.Add(addRange);

        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(rangesPanel);

        var row = new ChannelRow
        {
            Root = root, Number = number, TypeBox = typeBox, NameBox = name, DefaultBox = defaultBox,
            Ranges = ranges, RangesToggle = rangesToggle, OriginalName = existing?.Name,
        };
        void UpdateToggle()
        {
            int count = ranges.Children.Count;
            rangesToggle.Content = count == 0 ? "Ranges" : $"Ranges ({count})";
        }
        rangesToggle.Click += (_, _) =>
        {
            rangesPanel.Visibility = rangesToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (rangesToggle.IsChecked == true && ranges.Children.Count == 0) AddRange(ranges, 0, 255, "", UpdateToggle);
        };
        addRange.Click += (_, _) =>
        {
            // Continue where the last range ended.
            int from = row.Capabilities().Select(c => c.To + 1).DefaultIfEmpty(0).Max();
            AddRange(ranges, Math.Min(from, 255), 255, "", UpdateToggle);
        };
        remove.Click += (_, _) =>
        {
            _rows.Children.Remove(root);
            _channelRows.Remove(row);
            Renumber();
        };
        foreach (var cap in existing?.Capabilities ?? new())
            AddRange(ranges, cap.From, cap.To, cap.Label, UpdateToggle);
        _channelRows.Add(row);
        _rows.Children.Add(root);
        Renumber();
    }

    private static void AddRange(StackPanel ranges, int from, int to, string label, Action changed)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fromBox = ByteBox(from, ranges.Children.Count == 0 ? "From" : null);
        var toBox = ByteBox(to, ranges.Children.Count == 0 ? "To" : null);
        var text = new TextBox
        {
            Text = label, PlaceholderText = "What it does, e.g. Gobo 3 or Strobe slow → fast",
            Header = ranges.Children.Count == 0 ? "Description" : null,
        };
        var remove = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Padding = new Thickness(6, 5, 6, 5),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        Grid.SetColumn(toBox, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(remove, 3);
        grid.Children.Add(fromBox);
        grid.Children.Add(toBox);
        grid.Children.Add(text);
        grid.Children.Add(remove);
        remove.Click += (_, _) =>
        {
            ranges.Children.Remove(grid);
            changed();
        };
        ranges.Children.Add(grid);
        changed();
    }

    private void Renumber()
    {
        int i = 1;
        foreach (var row in _channelRows) row.Number.Text = $"{i++}.";
        _modeName.PlaceholderText = $"{_channelRows.Count}-channel";
    }

    private static string Pretty(ChannelType type) =>
        type == ChannelType.UV ? "UV" : Regex.Replace(type.ToString(), "(?<!^)([A-Z])", " $1");

    private IEnumerable<(ChannelType Type, string Name)> Channels() => _channelRows.Select(r => (r.Type, r.Name));

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(_model.Text)) return "Please enter the model name.";
        var channels = Channels().ToList();
        if (channels.Count == 0) return "Add at least one channel.";
        if (channels.Count > 512) return "A fixture can't use more than 512 channels.";
        var duplicate = channels.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) return $"Two channels are called \"{duplicate.Key}\". Give each channel its own name.";
        foreach (var row in _channelRows)
        {
            var problem = ChannelCapability.Validate(row.Capabilities());
            if (problem is not null)
            {
                row.RangesToggle.IsChecked = true;
                ((FrameworkElement)row.Root.Children[1]).Visibility = Visibility.Visible;
                return $"Channel {row.Number.Text} {row.Name}: {problem}";
            }
        }
        return null;
    }

    /// <summary>The new channel list, each row remembering the channel it was loaded from.</summary>
    private List<EditedChannel> EditedChannels() =>
        _channelRows.Select(row => new EditedChannel(row.OriginalName, new ChannelDefinition
        {
            Name = row.Name,
            Type = row.Type,
            Default = (byte)Math.Clamp(ToByte(row.DefaultBox.Value), 0, 255),
            Capabilities = row.Capabilities().OrderBy(c => c.From).ToList(),
        })).ToList();

    private FixtureDefinition Build()
    {
        var manufacturer = string.IsNullOrWhiteSpace(_manufacturer.Text) ? "My fixtures" : _manufacturer.Text.Trim();
        var model = _model.Text.Trim();
        static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        var def = new FixtureDefinition
        {
            Id = $"user/{Slug(manufacturer)}/{Slug(model)}",
            Manufacturer = manufacturer,
            Model = model,
            Category = _category.SelectedItem as string ?? "Other",
            IsUserDefined = true,
        };
        var mode = new FixtureMode
        {
            Name = string.IsNullOrWhiteSpace(_modeName.Text) ? $"{_channelRows.Count}-channel" : _modeName.Text.Trim(),
        };
        foreach (var channel in EditedChannels().Select(c => c.Channel))
        {
            def.Channels.Add(channel);
            mode.Channels.Add(channel.Name);
        }
        def.Modes.Add(mode);
        return def;
    }

    /// <summary>Shows the editor; on save the fixture goes into the user's library and can be added straight away.</summary>
    public static async Task CreateAsync()
    {
        var dialog = new FixtureEditorDialog();
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;

        var def = dialog.Build();
        try
        {
            FixtureLibrary.SaveUserFixture(AppPaths.UserFixtures, def);
        }
        catch (IOException ex)
        {
            await DialogHelper.ShowMessageAsync("Could not save the fixture", ex.Message);
            return;
        }
        AppServices.Library.Add(def);

        if (await DialogHelper.ConfirmAsync("Fixture saved",
                $"\"{def.DisplayName}\" is now in your library (saved in Documents\\LuxEngine\\Fixtures). Add it to the show now?", "Add now"))
            await AddFixtureDialog.ShowAndAddAsync();
    }

    /// <summary>
    /// Edits the type of a fixture in the show. Every fixture of that type and mode changes with it,
    /// and presets follow renamed channels.
    /// </summary>
    public static async Task EditAsync(Guid fixtureId)
    {
        var show = AppServices.Engine.Show;
        var fixture = show.FindFixture(fixtureId);
        var original = fixture is null ? null : show.DefinitionOf(fixture);
        if (fixture is null || original is null) return;

        var dialog = new FixtureEditorDialog(original, fixture.ModeName);
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;

        var newModeName = string.IsNullOrWhiteSpace(dialog._modeName.Text) ? $"{dialog._channelRows.Count}-channel" : dialog._modeName.Text.Trim();
        var (def, renames) = FixtureTypeEditing.EditMode(original, fixture.ModeName, newModeName, dialog.EditedChannels(),
            string.IsNullOrWhiteSpace(dialog._manufacturer.Text) ? original.Manufacturer : dialog._manufacturer.Text.Trim(),
            dialog._model.Text.Trim(), dialog._category.SelectedItem as string ?? original.Category);

        int conflictsBefore = Core.Shows.PatchHelper.FindConflicts(show).Count;
        AppServices.Document.Edit(ShowChange.Patch | ShowChange.Presets, s => s.UpdateDefinition(def, fixture.ModeName, newModeName, renames));

        if (dialog._saveToLibrary.IsChecked == true)
        {
            try
            {
                FixtureLibrary.SaveUserFixture(AppPaths.UserFixtures, def);
                AppServices.Library.Add(def);
            }
            catch (IOException ex)
            {
                await DialogHelper.ShowMessageAsync("The show was updated, but the library copy could not be saved", ex.Message);
            }
        }

        var conflicts = Core.Shows.PatchHelper.FindConflicts(AppServices.Engine.Show).Count;
        if (conflicts > conflictsBefore)
            await DialogHelper.ShowMessageAsync("Check your addresses",
                $"The fixture now uses a different number of channels, so {conflicts} fixture(s) overlap. Change their start addresses with \"Edit name and address…\".");
    }
}
