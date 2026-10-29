using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuxEngine.App.Dialogs;

/// <summary>Rename a patched fixture or change its mode and DMX address.</summary>
public static class FixturePropertiesDialog
{
    public static async Task EditAsync(Guid fixtureId)
    {
        var show = AppServices.Engine.Show;
        var fixture = show.FindFixture(fixtureId);
        if (fixture is null) return;
        var def = show.DefinitionOf(fixture);

        var name = new TextBox { Header = "Name", Text = fixture.Name };
        var mode = new ComboBox { Header = "Mode", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var m in def?.Modes ?? new()) mode.Items.Add(m.Name);
        mode.SelectedItem = fixture.ModeName;
        var universe = new NumberBox { Header = "Universe", Value = fixture.Universe, Minimum = 1, Maximum = 64000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var address = new NumberBox { Header = "Start address", Value = fixture.Address, Minimum = 1, Maximum = 512, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

        var numbers = new Grid { ColumnSpacing = 8 };
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(address, 1);
        numbers.Children.Add(universe);
        numbers.Children.Add(address);

        bool moves = def?.Channels.Any(c => c.Type is Core.Fixtures.ChannelType.Pan or Core.Fixtures.ChannelType.Tilt) == true;
        var invertPan = new CheckBox { Content = "Invert pan (left ↔ right)", IsChecked = fixture.InvertPan };
        var invertTilt = new CheckBox { Content = "Invert tilt (up ↔ down)", IsChecked = fixture.InvertTilt };

        var panel = new StackPanel { Spacing = 12, Width = 380 };
        panel.Children.Add(new TextBlock { Text = def?.DisplayName ?? "Unknown fixture type", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        panel.Children.Add(name);
        panel.Children.Add(mode);
        panel.Children.Add(numbers);
        if (moves)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "For heads hung upside down or facing the other way. Only the signal to the light is flipped; the controls keep working the normal way.",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
            panel.Children.Add(invertPan);
            panel.Children.Add(invertTilt);
        }

        var dialog = new ContentDialog
        {
            Title = "Edit fixture",
            Content = panel,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Edit fixture type…",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await DialogHelper.ShowAsync(dialog);
        if (result == ContentDialogResult.Secondary)
        {
            await FixtureEditorDialog.EditAsync(fixtureId);
            return;
        }
        if (result != ContentDialogResult.Primary) return;

        AppServices.Document.Edit(ShowChange.Patch, _ =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text)) fixture.Name = name.Text.Trim();
            if (mode.SelectedItem is string m) fixture.ModeName = m;
            if (!double.IsNaN(universe.Value)) fixture.Universe = (int)universe.Value;
            if (!double.IsNaN(address.Value)) fixture.Address = (int)address.Value;
            fixture.InvertPan = invertPan.IsChecked == true;
            fixture.InvertTilt = invertTilt.IsChecked == true;
        });
    }
}
