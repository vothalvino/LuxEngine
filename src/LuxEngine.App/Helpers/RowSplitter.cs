using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;

namespace LuxEngine.App.Helpers;

/// <summary>A thin drag handle that resizes the grid row below it (the Control / Cue area).</summary>
public sealed class RowSplitter : Grid
{
    private readonly Rectangle _grip = new() { Width = 44, Height = 4, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private double _startY, _startHeight;
    private bool _dragging;

    public RowSplitter()
    {
        Height = 12;
        Margin = new Thickness(0, -8, 0, -8);
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Children.Add(_grip);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        ToolTipService.SetToolTip(this, "Drag to make the Control / Cue area bigger or smaller");
        Loaded += (_, _) => _grip.Fill = Ui.Brush("ControlStrongFillColorDefaultBrush");
        Ui.ThemeChanged += () => _grip.Fill = Ui.Brush("ControlStrongFillColorDefaultBrush");
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => _dragging = false;
    }

    /// <summary>The row whose height the handle changes.</summary>
    public RowDefinition? Target { get; set; }
    public double MinSize { get; set; } = 220;
    public double MaxSize { get; set; } = 900;

    /// <summary>Raised when a drag ends, with the new height (to remember it).</summary>
    public event Action<double>? Resized;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Target is null) return;
        _dragging = true;
        _startY = e.GetCurrentPoint(null).Position.Y;
        _startHeight = Target.ActualHeight;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || Target is null) return;
        // Dragging up makes the area below bigger.
        double height = Math.Clamp(_startHeight - (e.GetCurrentPoint(null).Position.Y - _startY), MinSize, MaxSize);
        Target.Height = new GridLength(height);
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        if (Target is not null) Resized?.Invoke(Target.ActualHeight);
    }
}
