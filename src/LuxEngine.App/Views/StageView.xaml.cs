using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace LuxEngine.App.Views;

/// <summary>A 2D picture of the stage showing every light's live color. Click or drag to select lights.</summary>
public sealed partial class StageView : UserControl
{
    private const double BodySize = 30;
    private const double GlowSize = 76;

    private sealed class FixtureVisual
    {
        public required Guid Id;
        public required Grid Root;
        public required Ellipse Glow;
        public required GradientStop GlowCenter;
        public required Ellipse Body;
        public required Ellipse Beam;
        public required TextBlock Label;
    }

    private readonly Dictionary<Guid, FixtureVisual> _visuals = new();
    private readonly DispatcherQueueTimer _timer;
    private Point _pressPoint;
    private bool _rubberBand;
    private Guid? _dragFixture;
    private Dictionary<Guid, Point>? _dragStart;
    private bool _moved;

    public StageView()
    {
        InitializeComponent();
        AppServices.Document.Changed += change =>
        {
            if (change.HasFlag(ShowChange.Patch)) Rebuild();
        };
        AppServices.Selection.Changed += UpdateSelection;
        Loaded += (_, _) => Rebuild();

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => UpdateColors();
        _timer.Start();
    }

    private void Rebuild()
    {
        StageCanvas.Children.Clear();
        _visuals.Clear();
        foreach (var fixture in AppServices.Engine.Show.Fixtures)
        {
            var center = new GradientStop { Color = Colors.Transparent, Offset = 0 };
            var glow = new Ellipse
            {
                Width = GlowSize, Height = GlowSize, IsHitTestVisible = false,
                Fill = new RadialGradientBrush
                {
                    GradientStops = { center, new GradientStop { Color = Colors.Transparent, Offset = 1 } },
                },
            };
            var body = new Ellipse
            {
                Width = BodySize, Height = BodySize,
                Fill = new SolidColorBrush(Color.FromArgb(255, 30, 30, 36)),
                StrokeThickness = 1.5,
                Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var beam = new Ellipse
            {
                Width = 6, Height = 6, Fill = new SolidColorBrush(Colors.White), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            var label = new TextBlock
            {
                Text = fixture.Name, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 90, IsHitTestVisible = false,
                Margin = new Thickness(0, 0, 0, -4),
            };
            var root = new Grid { Width = GlowSize, Height = GlowSize + 10, Tag = fixture.Id };
            root.Children.Add(glow);
            root.Children.Add(body);
            root.Children.Add(beam);
            root.Children.Add(label);
            ToolTipService.SetToolTip(root, $"{fixture.Name}\nUniverse {fixture.Universe}, address {fixture.Address}");
            StageCanvas.Children.Add(root);
            _visuals[fixture.Id] = new FixtureVisual { Id = fixture.Id, Root = root, Glow = glow, GlowCenter = center, Body = body, Beam = beam, Label = label };
        }
        EmptyText.Visibility = _visuals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Layout();
        UpdateSelection();
    }

    private void Layout()
    {
        double w = StageRoot.ActualWidth, h = StageRoot.ActualHeight;
        if (w <= 0 || h <= 0) return;
        foreach (var fixture in AppServices.Engine.Show.Fixtures)
        {
            if (!_visuals.TryGetValue(fixture.Id, out var v)) continue;
            Canvas.SetLeft(v.Root, Math.Clamp(fixture.StageX, 0, 1) * (w - GlowSize));
            Canvas.SetTop(v.Root, Math.Clamp(fixture.StageY, 0, 1) * (h - GlowSize - 50) + 10);
        }
    }

    private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void UpdateSelection()
    {
        var accent = (Brush)Application.Current.Resources["LuxAccentBrush"];
        var normal = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        foreach (var v in _visuals.Values)
        {
            bool selected = AppServices.Selection.Contains(v.Id);
            v.Body.Stroke = selected ? accent : normal;
            v.Body.StrokeThickness = selected ? 3 : 1.5;
            v.Label.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void UpdateColors()
    {
        foreach (var v in _visuals.Values)
        {
            var look = AppServices.Engine.GetLook(v.Id);
            var lit = ColorMixer.FromLook(look);
            ((SolidColorBrush)v.Body.Fill).Color = Color.FromArgb(255,
                (byte)Math.Max(30, (int)lit.R), (byte)Math.Max(30, (int)lit.G), (byte)Math.Max(36, (int)lit.B));
            var full = ColorMixer.FromLook(look with { Intensity = 1 });
            v.GlowCenter.Color = Color.FromArgb((byte)(Math.Clamp(look.Intensity, 0, 1) * 220), full.R, full.G, full.B);

            if (look.Pan is double pan && look.Tilt is double tilt)
            {
                v.Beam.Visibility = Visibility.Visible;
                v.Beam.Translation = new System.Numerics.Vector3((float)((pan - 0.5) * BodySize * 0.9), (float)((tilt - 0.5) * BodySize * 0.9), 0);
            }
            else
            {
                v.Beam.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ------------------------------------------------------------------ pointer handling

    private Guid? HitFixture(object source)
    {
        var element = source as DependencyObject;
        while (element is not null && element != StageRoot)
        {
            if (element is FrameworkElement { Tag: Guid id }) return id;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static bool CtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void Stage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not (Grid or Ellipse or Canvas or Border or TextBlock or Rectangle)) return;
        _pressPoint = e.GetCurrentPoint(StageRoot).Position;
        _moved = false;
        var hit = HitFixture(e.OriginalSource);

        if (hit is Guid id)
        {
            if (CtrlDown()) AppServices.Selection.Toggle(id);
            else if (!AppServices.Selection.Contains(id) || ArrangeButton.IsChecked != true) AppServices.Selection.Set(new[] { id });

            if (ArrangeButton.IsChecked == true)
            {
                _dragFixture = id;
                _dragStart = AppServices.Engine.Show.Fixtures
                    .Where(f => AppServices.Selection.Contains(f.Id))
                    .ToDictionary(f => f.Id, f => new Point(f.StageX, f.StageY));
                StageRoot.CapturePointer(e.Pointer);
            }
        }
        else
        {
            _rubberBand = true;
            StageRoot.CapturePointer(e.Pointer);
        }
        e.Handled = true;
    }

    private void Stage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(StageRoot).Position;
        if (Math.Abs(p.X - _pressPoint.X) + Math.Abs(p.Y - _pressPoint.Y) > 4) _moved = true;

        if (_rubberBand && _moved)
        {
            SelectionRect.Visibility = Visibility.Visible;
            SelectionRect.Margin = new Thickness(Math.Min(p.X, _pressPoint.X), Math.Min(p.Y, _pressPoint.Y), 0, 0);
            SelectionRect.Width = Math.Abs(p.X - _pressPoint.X);
            SelectionRect.Height = Math.Abs(p.Y - _pressPoint.Y);
        }
        else if (_dragFixture is not null && _dragStart is not null && _moved)
        {
            double w = Math.Max(1, StageRoot.ActualWidth - GlowSize), h = Math.Max(1, StageRoot.ActualHeight - GlowSize - 50);
            double dx = (p.X - _pressPoint.X) / w, dy = (p.Y - _pressPoint.Y) / h;
            foreach (var fixture in AppServices.Engine.Show.Fixtures)
            {
                if (!_dragStart.TryGetValue(fixture.Id, out var start)) continue;
                fixture.StageX = Math.Clamp(start.X + dx, 0, 1);
                fixture.StageY = Math.Clamp(start.Y + dy, 0, 1);
            }
            Layout();
        }
    }

    private void Stage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_rubberBand)
        {
            if (_moved)
            {
                var rect = new Rect(SelectionRect.Margin.Left, SelectionRect.Margin.Top, SelectionRect.Width, SelectionRect.Height);
                var inside = _visuals.Values
                    .Where(v =>
                    {
                        double cx = Canvas.GetLeft(v.Root) + GlowSize / 2, cy = Canvas.GetTop(v.Root) + GlowSize / 2;
                        return rect.Contains(new Point(cx, cy));
                    })
                    .Select(v => v.Id);
                AppServices.Selection.Set(CtrlDown() ? AppServices.Selection.Ids.Concat(inside) : inside);
            }
            else if (!CtrlDown())
            {
                AppServices.Selection.Clear();
            }
        }
        else if (_dragFixture is not null && _moved)
        {
            AppServices.Document.MarkDirty();
        }

        _rubberBand = false;
        _dragFixture = null;
        _dragStart = null;
        SelectionRect.Visibility = Visibility.Collapsed;
        StageRoot.ReleasePointerCaptures();
    }

    /// <summary>Places fixtures in rows: one row per fixture type, evenly spread.</summary>
    private void AutoArrange_Click(object sender, RoutedEventArgs e)
    {
        var groups = AppServices.Engine.Show.Fixtures.GroupBy(f => f.DefinitionId).ToList();
        for (int row = 0; row < groups.Count; row++)
        {
            var items = groups[row].ToList();
            double y = groups.Count == 1 ? 0.4 : (double)row / (groups.Count - 1);
            for (int i = 0; i < items.Count; i++)
            {
                items[i].StageX = items.Count == 1 ? 0.5 : (double)i / (items.Count - 1);
                items[i].StageY = y;
            }
        }
        AppServices.Document.MarkDirty();
        Layout();
    }
}
