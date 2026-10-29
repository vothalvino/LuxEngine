using LuxEngine.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LuxEngine.App.Views;

/// <summary>
/// A wide vertical fader (0–100%) with an accent-filled track, tick marks and a large value.
/// Drag or click the track, use the mouse wheel, or double-click the value to type a number.
/// </summary>
public sealed class LevelFader : UserControl
{
    private const double TrackHeight = 170, TrackWidth = 46;

    private readonly Grid _track = new() { Width = TrackWidth, Height = TrackHeight, CornerRadius = new CornerRadius(10) };
    private readonly Border _fill = new() { VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 10, 10) };
    private readonly Rectangle _thumb = new() { Height = 6, RadiusX = 3, RadiusY = 3, Margin = new Thickness(-6, 0, -6, 0), VerticalAlignment = VerticalAlignment.Bottom, StrokeThickness = 1.5 };
    private readonly TextBlock _text = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly List<Rectangle> _ticks = new();
    private double _value;
    private bool _dragging;

    public LevelFader()
    {
        _track.Children.Add(_fill);
        foreach (var f in new[] { 0.25, 0.5, 0.75 })
        {
            var tick = new Rectangle { Height = 1.5, Width = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, f * TrackHeight), IsHitTestVisible = false };
            _ticks.Add(tick);
            _track.Children.Add(tick);
        }
        _track.Children.Add(_thumb);

        ToolTipService.SetToolTip(_text, "Double-click to type a value");
        var panel = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(_track);
        panel.Children.Add(_text);
        Content = panel;

        _track.PointerPressed += (_, e) =>
        {
            _dragging = true;
            _track.CapturePointer(e.Pointer);
            SetFromPointer(e);
            e.Handled = true;
        };
        _track.PointerMoved += (_, e) => { if (_dragging) SetFromPointer(e); };
        _track.PointerReleased += (_, e) =>
        {
            _dragging = false;
            _track.ReleasePointerCapture(e.Pointer);
        };
        _track.PointerCaptureLost += (_, _) => _dragging = false;
        _track.PointerWheelChanged += (_, e) =>
        {
            Value += e.GetCurrentPoint(_track).Properties.MouseWheelDelta > 0 ? 5 : -5;
            e.Handled = true;
        };
        _text.DoubleTapped += (_, _) => TypeValue();

        Loaded += (_, _) => ApplyColors();
        Ui.ThemeChanged += ApplyColors;
        Update();
    }

    /// <summary>0–100.</summary>
    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Round(Math.Clamp(value, 0, 100));
            if (v == _value) return;
            _value = v;
            Update();
            ValueChanged?.Invoke(this, v);
        }
    }

    public event EventHandler<double>? ValueChanged;

    private void ApplyColors()
    {
        _track.Background = Ui.Brush("LuxTrackBrush");
        _fill.Background = Ui.Brush("LuxAccentBrush");
        _thumb.Fill = Ui.Brush("SolidBackgroundFillColorBaseBrush");
        _thumb.Stroke = Ui.Brush("LuxAccentBrush");
        foreach (var t in _ticks) t.Fill = Ui.Brush("TextFillColorTertiaryBrush");
    }

    private void Update()
    {
        double h = _value / 100 * TrackHeight;
        _fill.Height = h;
        _thumb.Margin = new Thickness(-6, 0, -6, Math.Clamp(h - 3, 0, TrackHeight - 6));
        _text.Text = $"{_value:0}%";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"Dimmer {_value:0}%");
    }

    private void SetFromPointer(PointerRoutedEventArgs e)
    {
        double y = e.GetCurrentPoint(_track).Position.Y;
        Value = (1 - y / TrackHeight) * 100;
    }

    private void TypeValue()
    {
        var box = new NumberBox { Minimum = 0, Maximum = 100, Value = _value, Width = 120, Header = "Dimmer (%)", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var flyout = new Flyout { Content = box };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!double.IsNaN(box.Value)) Value = box.Value;
            flyout.Hide();
        };
        flyout.Closed += (_, _) => { if (!double.IsNaN(box.Value)) Value = box.Value; };
        flyout.ShowAt(_text);
        box.Focus(FocusState.Programmatic);
    }
}
