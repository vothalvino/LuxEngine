using System.Text.RegularExpressions;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Fixtures;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuxEngine.App.Dialogs;

/// <summary>Create a fixture type by hand: name it and list its channels in DMX order.</summary>
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
    private readonly TextBlock _error = new() { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LuxDangerBrush"], TextWrapping = TextWrapping.Wrap };

    private FixtureEditorDialog()
    {
        Title = "Create a fixture type";
        PrimaryButtonText = "Save fixture";
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

        var layout = new StackPanel { Spacing = 12, Width = 640 };
        layout.Children.Add(header);
        layout.Children.Add(_modeName);
        layout.Children.Add(new TextBlock
        {
            Text = "Channels in DMX order (channel 1 first). Check the manual of your light for this list.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        layout.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 360 });
        layout.Children.Add(buttons);
        layout.Children.Add(_error);
        Content = layout;

        AddRow(ChannelType.Intensity);
        AddRow(ChannelType.Red);
        AddRow(ChannelType.Green);
        AddRow(ChannelType.Blue);

        PrimaryButtonClick += (_, args) =>
        {
            var problem = Validate();
            _error.Text = problem ?? "";
            args.Cancel = problem is not null;
        };
    }

    private void AddRow(ChannelType type)
    {
        var number = new TextBlock { Width = 28, VerticalAlignment = VerticalAlignment.Center };
        var typeBox = new ComboBox { Width = 190 };
        foreach (var t in Enum.GetValues<ChannelType>()) typeBox.Items.Add(new ComboBoxItem { Content = Pretty(t), Tag = t });
        typeBox.SelectedIndex = (int)type;
        var name = new TextBox { Width = 230, PlaceholderText = "Channel name", Text = Pretty(type) };
        typeBox.SelectionChanged += (_, _) =>
        {
            // Keep the name in sync until the user types their own.
            if (typeBox.SelectedItem is ComboBoxItem { Tag: ChannelType t } && (name.Text.Length == 0 || Enum.GetValues<ChannelType>().Any(x => Pretty(x) == name.Text)))
                name.Text = Pretty(t);
        };
        var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 }, Padding = new Thickness(8, 6, 8, 6) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(number);
        row.Children.Add(typeBox);
        row.Children.Add(name);
        row.Children.Add(remove);
        remove.Click += (_, _) =>
        {
            _rows.Children.Remove(row);
            Renumber();
        };
        _rows.Children.Add(row);
        Renumber();
    }

    private void Renumber()
    {
        int i = 1;
        foreach (var row in _rows.Children.OfType<StackPanel>())
            ((TextBlock)row.Children[0]).Text = $"{i++}.";
        _modeName.PlaceholderText = $"{_rows.Children.Count}-channel";
    }

    private static string Pretty(ChannelType type) =>
        type == ChannelType.UV ? "UV" : Regex.Replace(type.ToString(), "(?<!^)([A-Z])", " $1");

    private IEnumerable<(ChannelType Type, string Name)> Channels() =>
        _rows.Children.OfType<StackPanel>().Select(row =>
        {
            var type = ((row.Children[1] as ComboBox)?.SelectedItem as ComboBoxItem)?.Tag is ChannelType t ? t : ChannelType.Generic;
            var name = ((TextBox)row.Children[2]).Text.Trim();
            return (type, name.Length == 0 ? Pretty(type) : name);
        });

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(_model.Text)) return "Please enter the model name.";
        var channels = Channels().ToList();
        if (channels.Count == 0) return "Add at least one channel.";
        if (channels.Count > 512) return "A fixture can't use more than 512 channels.";
        var duplicate = channels.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) return $"Two channels are called \"{duplicate.Key}\". Give each channel its own name.";
        return null;
    }

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
        var channels = Channels().ToList();
        var mode = new FixtureMode
        {
            Name = string.IsNullOrWhiteSpace(_modeName.Text) ? $"{channels.Count}-channel" : _modeName.Text.Trim(),
        };
        foreach (var (type, name) in channels)
        {
            def.Channels.Add(new ChannelDefinition
            {
                Name = name,
                Type = type,
                Default = type is ChannelType.Pan or ChannelType.Tilt ? (byte)128 : (byte)0,
            });
            mode.Channels.Add(name);
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
}
