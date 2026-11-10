using Lumeshow.App.Helpers;
using Lumeshow.App.Services;
using Lumeshow.Core.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lumeshow.App.Dialogs;

/// <summary>Changes an effect that is already running or saved in a preset: speed, size, spread and, for movement, shape, area, width, height and direction.</summary>
public static class EffectEditDialog
{
    public static async Task EditAsync(Guid effectId)
    {
        var show = AppServices.Engine.Show;
        var effect = AppServices.Engine.ProgrammerSnapshot().Effects.FirstOrDefault(e => e.Id == effectId);
        if (effect is null) return;
        bool movement = effect.Type.IsMovement();

        Slider SliderFor(double min, double max, double value) => new() { Minimum = min, Maximum = max, StepFrequency = (max - min) / 100, Value = value, Width = 220 };
        var speed = SliderFor(0.02, movement ? 2 : 5, effect.Speed);
        var size = SliderFor(0, 1, effect.Size);
        var spread = SliderFor(0, 1, effect.Spread);
        var width = SliderFor(0, 1, effect.Width);
        var height = SliderFor(0, 1, effect.Height);
        var reverse = new CheckBox { Content = "Reverse direction", IsChecked = effect.Reverse };

        // "To the beat": the effect follows the tapped tempo instead of the speed slider.
        var beatChoices = new (string Label, double Beats)[] { ("¼ beat", 0.25), ("½ beat", 0.5), ("1 beat", 1), ("2 beats", 2), ("4 beats", 4), ("8 beats", 8), ("16 beats", 16) };
        var sync = new CheckBox { Content = "To the beat", IsChecked = effect.Sync, MinWidth = 0 };
        var beats = new ComboBox { MinWidth = 104, IsEnabled = effect.Sync };
        foreach (var choice in beatChoices) beats.Items.Add(choice.Label);
        beats.SelectedIndex = Math.Max(0, Array.FindIndex(beatChoices, c => Math.Abs(c.Beats - effect.Beats) < 0.001));
        if (Array.FindIndex(beatChoices, c => Math.Abs(c.Beats - effect.Beats) < 0.001) < 0) beats.SelectedIndex = 4;
        speed.IsEnabled = !effect.Sync;
        void SyncChanged(object sender, RoutedEventArgs e)
        {
            speed.IsEnabled = sync.IsChecked != true;
            beats.IsEnabled = sync.IsChecked == true;
        }
        sync.Checked += SyncChanged;
        sync.Unchecked += SyncChanged;
        var beatRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        beatRow.Children.Add(sync);
        beatRow.Children.Add(beats);

        var shapes = new List<(string Label, EffectType Type, Guid? Id)>();
        foreach (var t in new[] { EffectType.Circle, EffectType.Figure8, EffectType.PanSweep, EffectType.TiltSweep }) shapes.Add((t.DisplayName(), t, null));
        foreach (var s in show.Shapes) shapes.Add(($"★ {s.Name}", EffectType.Shape, s.Id));
        var shapeBox = new ComboBox { Header = "Shape", Width = 220 };
        foreach (var s in shapes) shapeBox.Items.Add(s.Label);
        shapeBox.SelectedIndex = Math.Max(0, shapes.FindIndex(s => s.Type == effect.Type && s.Id == effect.ShapeId));

        var areas = new List<(string Label, Guid? Id)> { ("No area (free)", null) };
        areas.AddRange(show.Areas.Select(a => (a.Name, (Guid?)a.Id)));
        var areaBox = new ComboBox { Header = "Area", Width = 220 };
        foreach (var a in areas) areaBox.Items.Add(a.Label);
        areaBox.SelectedIndex = Math.Max(0, areas.FindIndex(a => a.Id == effect.AreaId));

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string label, FrameworkElement control)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int r = grid.RowDefinitions.Count - 1;
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, r);
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            grid.Children.Add(text);
            grid.Children.Add(control);
        }
        Row("Speed", speed);
        Row("", beatRow);
        Row("Size", size);
        if (movement)
        {
            Row("Width", width);
            Row("Height", height);
        }
        Row("Spread", spread);

        var panel = new StackPanel { Spacing = 10, Width = 340 };
        if (movement)
        {
            panel.Children.Add(shapeBox);
            panel.Children.Add(areaBox);
        }
        panel.Children.Add(grid);
        if (movement) panel.Children.Add(reverse);

        var dialog = new ContentDialog
        {
            Title = $"Edit {Core.Presets.PresetNamer.EffectName(show, effect)}",
            Content = panel,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;

        AppServices.Engine.UpdateEffect(effectId, e =>
        {
            e.Speed = speed.Value;
            e.Sync = sync.IsChecked == true;
            e.Beats = beatChoices[Math.Max(0, beats.SelectedIndex)].Beats;
            e.Size = size.Value;
            e.Spread = spread.Value;
            if (!movement) return;
            var shape = shapes[Math.Max(0, shapeBox.SelectedIndex)];
            e.Type = shape.Type;
            e.ShapeId = shape.Id;
            e.AreaId = areas[Math.Max(0, areaBox.SelectedIndex)].Id;
            e.Width = width.Value;
            e.Height = height.Value;
            e.Reverse = reverse.IsChecked == true;
        });
    }
}
