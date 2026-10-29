using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuxEngine.App.Dialogs;

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

        var shapes = new List<(string Label, EffectType Type, Guid? Id)>();
        foreach (var t in new[] { EffectType.Circle, EffectType.Figure8, EffectType.PanSweep, EffectType.TiltSweep }) shapes.Add((t.DisplayName(), t, null));
        foreach (var s in show.Shapes) shapes.Add(($"★ {s.Name}", EffectType.Shape, s.Id));
        var shapeBox = new ComboBox { Header = "Shape", Width = 220 };
        foreach (var s in shapes) shapeBox.Items.Add(s.Label);
        shapeBox.SelectedIndex = Math.Max(0, shapes.FindIndex(s => s.Type == effect.Type && s.Id == effect.ShapeId));

        var areas = new List<(string Label, Guid? Id)> { ("Around where it points", null) };
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
