using LuxEngine.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LuxEngine.App.Dialogs;

/// <summary>Shows all 512 channel values of a universe, live. Handy to check what is being sent.</summary>
public sealed class UniverseMonitorDialog : ContentDialog
{
    private const int Columns = 32;
    private readonly TextBlock[] _cells = new TextBlock[512];
    private readonly Border[] _backgrounds = new Border[512];
    private readonly NumberBox _universe = new() { Header = "Universe", Value = 1, Minimum = 1, Maximum = 64000, Width = 120, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly DispatcherQueueTimer _timer;
    private readonly Dictionary<int, string> _owners = new();

    public UniverseMonitorDialog()
    {
        Title = "Universe monitor";
        CloseButtonText = "Close";

        var grid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
        for (int c = 0; c < Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        for (int r = 0; r < 512 / Columns; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });

        for (int i = 0; i < 512; i++)
        {
            var text = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
            var cell = new Border { Child = text, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)) };
            Grid.SetRow(cell, i / Columns);
            Grid.SetColumn(cell, i % Columns);
            grid.Children.Add(cell);
            _cells[i] = text;
            _backgrounds[i] = cell;
        }

        _universe.ValueChanged += (_, _) => UpdateOwners();
        var layout = new StackPanel { Spacing = 12 };
        layout.Children.Add(_universe);
        layout.Children.Add(new TextBlock
        {
            Text = "Each box is one DMX channel (1–512). Brighter boxes have higher values. Hover a box to see which light uses it.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        layout.Children.Add(grid);
        Content = layout;

        UpdateOwners();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private int Universe => double.IsNaN(_universe.Value) ? 1 : (int)_universe.Value;

    private void UpdateOwners()
    {
        _owners.Clear();
        foreach (var p in AppServices.Engine.Fixtures.Values.Where(p => p.Instance.Universe == Universe))
        foreach (var ch in p.Channels)
            _owners[ch.Address] = $"{p.Instance.Name}: {ch.Channel.Name}";
        for (int i = 0; i < 512; i++)
            ToolTipService.SetToolTip(_backgrounds[i], $"Channel {i + 1}" + (_owners.TryGetValue(i + 1, out var o) ? $"\n{o}" : "\n(not used)"));
    }

    private void Tick()
    {
        var frame = AppServices.Output.LastFrame ?? AppServices.Engine.ComputeFrame();
        frame.Universes.TryGetValue(Universe, out var data);
        for (int i = 0; i < 512; i++)
        {
            byte v = data?[i] ?? 0;
            _cells[i].Text = v.ToString();
            byte alpha = (byte)(30 + v * 0.7);
            ((SolidColorBrush)_backgrounds[i].Background).Color = _owners.ContainsKey(i + 1)
                ? Color.FromArgb(alpha, 255, 185, 56)
                : Color.FromArgb(30, 128, 128, 128);
        }
    }
}
