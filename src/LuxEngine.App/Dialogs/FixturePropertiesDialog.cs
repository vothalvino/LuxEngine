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

        var panel = new StackPanel { Spacing = 12, Width = 380 };
        panel.Children.Add(new TextBlock { Text = def?.DisplayName ?? "Unknown fixture type", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        panel.Children.Add(name);
        panel.Children.Add(mode);
        panel.Children.Add(numbers);

        var dialog = new ContentDialog
        {
            Title = "Edit fixture",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;

        AppServices.Document.Edit(ShowChange.Patch, _ =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text)) fixture.Name = name.Text.Trim();
            if (mode.SelectedItem is string m) fixture.ModeName = m;
            if (!double.IsNaN(universe.Value)) fixture.Universe = (int)universe.Value;
            if (!double.IsNaN(address.Value)) fixture.Address = (int)address.Value;
        });
    }
}
