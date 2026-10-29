using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Fixtures;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LuxEngine.App.Dialogs;

/// <summary>
/// The show's movement shapes: make them from templates, draw and change them point by point,
/// duplicate, rename and delete them, and try them on the selected moving heads.
/// </summary>
public sealed class ShapeLibraryDialog : ContentDialog
{
    private const double PadSize = 300;

    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Width = 200, Height = 300 };
    private readonly Canvas _pad = new() { Width = PadSize, Height = PadSize, Background = (Brush)Application.Current.Resources["LuxStageBrush"] };
    private readonly CheckBox _closed = new() { Content = "Closed loop" };
    private readonly CheckBox _smooth = new() { Content = "Smooth curve" };
    private readonly TextBlock _hint = new()
    {
        Text = "Click the pad to add a point, drag points to move them, right-click a point to remove it. Left/right is pan, up/down is tilt.",
        TextWrapping = TextWrapping.Wrap, Width = PadSize,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
    };
    private readonly Ellipse _previewDot = new() { Width = 12, Height = 12, Fill = new SolidColorBrush(Colors.White), IsHitTestVisible = false };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly Panel _editor;
    private Guid? _tryEffect;
    private Core.Engine.ProgrammerSnapshot? _beforeTry;
    private int? _dragging;
    private bool _loading;

    private ShapeLibraryDialog()
    {
        Title = "Movement shapes";
        CloseButtonText = "Done";

        var newButton = new DropDownButton { Content = "New" };
        var templates = new MenuFlyout();
        foreach (var template in MovementShape.Templates())
        {
            var item = new MenuFlyoutItem { Text = template.Name };
            item.Click += (_, _) => Add(template.Clone(UniqueName(template.Name)));
            templates.Items.Add(item);
        }
        var blank = new MenuFlyoutItem { Text = "Blank (draw your own)" };
        blank.Click += (_, _) => Add(new MovementShape { Name = UniqueName("My shape") });
        templates.Items.Add(new MenuFlyoutSeparator());
        templates.Items.Add(blank);
        newButton.Flyout = templates;

        var duplicate = new Button { Content = "Duplicate" };
        duplicate.Click += (_, _) => { if (Selected is { } s) Add(s.Clone(UniqueName(s.Name + " copy"))); };
        var rename = new Button { Content = "Rename" };
        rename.Click += (_, _) => Rename();
        var delete = new Button { Content = "Delete" };
        delete.Click += (_, _) => Delete();

        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        listButtons.Children.Add(newButton);
        listButtons.Children.Add(duplicate);
        var listButtons2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        listButtons2.Children.Add(rename);
        listButtons2.Children.Add(delete);
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(_list);
        left.Children.Add(listButtons);
        left.Children.Add(listButtons2);

        var tryButton = new ToggleButton { Content = "Try on selected moving heads" };
        ToolTipService.SetToolTip(tryButton, "Runs this shape on the selected moving heads while you edit it");
        tryButton.Click += (_, _) => SetTrying(tryButton.IsChecked == true);
        var clear = new Button { Content = "Clear points" };
        clear.Click += (_, _) => Change(s => s.Points = new List<ShapePoint>());
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        options.Children.Add(_closed);
        options.Children.Add(_smooth);
        options.Children.Add(clear);

        var editor = new StackPanel { Spacing = 8 };
        editor.Children.Add(new Border { Child = _pad, CornerRadius = new CornerRadius(6), Width = PadSize, Height = PadSize, HorizontalAlignment = HorizontalAlignment.Left });
        editor.Children.Add(options);
        editor.Children.Add(tryButton);
        editor.Children.Add(_hint);
        _editor = editor;

        var layout = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        layout.Children.Add(left);
        layout.Children.Add(editor);
        Content = layout;

        _list.SelectionChanged += (_, _) => Load();
        _closed.Click += (_, _) => { if (!_loading) Change(s => s.Closed = _closed.IsChecked == true); };
        _smooth.Click += (_, _) => { if (!_loading) Change(s => s.Smooth = _smooth.IsChecked == true); };
        _pad.PointerPressed += Pad_PointerPressed;
        _pad.PointerMoved += Pad_PointerMoved;
        _pad.PointerReleased += (_, e) => { _dragging = null; _pad.ReleasePointerCapture(e.Pointer); };

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        _timer.Tick += (_, _) => MovePreview();
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            SetTrying(false);
        };

        RefreshList(AppServices.Engine.Show.Shapes.FirstOrDefault()?.Id);
    }

    public static async Task ShowLibraryAsync() => await DialogHelper.ShowAsync(new ShapeLibraryDialog());

    private static List<MovementShape> Shapes => AppServices.Engine.Show.Shapes;

    private MovementShape? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < Shapes.Count ? Shapes[_list.SelectedIndex] : null;

    private static string UniqueName(string name)
    {
        var used = Shapes.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!used.Contains($"{name} {i}")) return $"{name} {i}";
    }

    private void RefreshList(Guid? select)
    {
        _list.Items.Clear();
        foreach (var s in Shapes) _list.Items.Add(s.Name);
        _list.SelectedIndex = Shapes.FindIndex(s => s.Id == select);
        if (_list.SelectedIndex < 0 && Shapes.Count > 0) _list.SelectedIndex = 0;
        Load();
    }

    private void Add(MovementShape shape)
    {
        AppServices.Engine.Edit(show => show.Shapes.Add(shape));
        AppServices.Document.MarkDirty();
        RefreshList(shape.Id);
    }

    private void Rename()
    {
        if (Selected is not { } shape) return;
        // A second dialog cannot open while this one is showing, so rename inline with a flyout.
        var box = new TextBox { Text = shape.Name, Width = 200 };
        var ok = new Button { Content = "Rename", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(ok);
        var flyout = new Flyout { Content = panel };
        ok.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            if (name.Length > 0) Change(s => s.Name = name);
            flyout.Hide();
            RefreshList(shape.Id);
        };
        flyout.ShowAt(_list);
    }

    private void Delete()
    {
        if (Selected is not { } shape) return;
        // Movements that used it fall back to a circle (see EffectEngine).
        AppServices.Engine.Edit(show => show.Shapes.Remove(shape));
        AppServices.Document.MarkDirty();
        RefreshList(null);
    }

    /// <summary>Edits the selected shape under the engine lock (the output thread reads shapes every frame).</summary>
    private void Change(Action<MovementShape> change)
    {
        if (Selected is not { } shape) return;
        AppServices.Engine.Edit(_ => change(shape));
        AppServices.Document.MarkDirty();
        Draw();
    }

    // ------------------------------------------------------------------ drawing

    private void Load()
    {
        _editor.Visibility = Selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (Selected is not { } shape) return;
        _loading = true;
        _closed.IsChecked = shape.Closed;
        _smooth.IsChecked = shape.Smooth;
        _loading = false;
        Draw();
        if (_tryEffect is Guid id) AppServices.Engine.UpdateEffect(id, e => e.ShapeId = shape.Id);
    }

    private static Point ToPad(ShapePoint p) => new((p.X + 1) / 2 * PadSize, (p.Y + 1) / 2 * PadSize);

    private static ShapePoint FromPad(Point p) => new(
        Math.Round(Math.Clamp(p.X / PadSize * 2 - 1, -1, 1), 3),
        Math.Round(Math.Clamp(p.Y / PadSize * 2 - 1, -1, 1), 3));

    private void Draw()
    {
        _pad.Children.Clear();
        var grid = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255));
        foreach (var f in new[] { 0.25, 0.5, 0.75 })
        {
            _pad.Children.Add(new Line { X1 = f * PadSize, X2 = f * PadSize, Y2 = PadSize, Stroke = grid, StrokeThickness = f == 0.5 ? 1.5 : 1 });
            _pad.Children.Add(new Line { Y1 = f * PadSize, Y2 = f * PadSize, X2 = PadSize, Stroke = grid, StrokeThickness = f == 0.5 ? 1.5 : 1 });
        }
        if (Selected is not { } shape) return;

        if (shape.Points.Count >= 2)
        {
            var line = new Polyline { Stroke = (Brush)Application.Current.Resources["LuxAccentBrush"], StrokeThickness = 2.5, IsHitTestVisible = false };
            foreach (var p in shape.Path()) line.Points.Add(ToPad(p));
            _pad.Children.Add(line);
        }
        for (int i = 0; i < shape.Points.Count; i++)
        {
            var at = ToPad(shape.Points[i]);
            var handle = new Ellipse
            {
                Width = 16, Height = 16, Tag = i,
                Fill = new SolidColorBrush(i == 0 ? Colors.Orange : Colors.White),
                Stroke = new SolidColorBrush(Colors.Black), StrokeThickness = 1,
            };
            ToolTipService.SetToolTip(handle, i == 0 ? "Start point" : $"Point {i + 1}");
            Canvas.SetLeft(handle, at.X - 8);
            Canvas.SetTop(handle, at.Y - 8);
            _pad.Children.Add(handle);
        }
        _pad.Children.Add(_previewDot);
    }

    private void MovePreview()
    {
        if (Selected is not { } shape || shape.Points.Count < 2)
        {
            _previewDot.Visibility = Visibility.Collapsed;
            return;
        }
        _previewDot.Visibility = Visibility.Visible;
        var (x, y) = shape.At(Environment.TickCount64 / 4000.0);   // one round every 4 seconds
        var at = ToPad(new ShapePoint(x, y));
        Canvas.SetLeft(_previewDot, at.X - 6);
        Canvas.SetTop(_previewDot, at.Y - 6);
    }

    private void Pad_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Selected is null) return;
        var point = e.GetCurrentPoint(_pad);
        if (e.OriginalSource is Ellipse { Tag: int index })
        {
            if (point.Properties.IsRightButtonPressed)
            {
                Change(s => s.Points = s.Points.Where((_, i) => i != index).ToList());
            }
            else
            {
                _dragging = index;
                _pad.CapturePointer(e.Pointer);
            }
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            var p = FromPad(point.Position);
            Change(s => s.Points = s.Points.Append(p).ToList());
        }
        e.Handled = true;
    }

    private void Pad_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is not int index) return;
        var p = FromPad(e.GetCurrentPoint(_pad).Position);
        Change(s =>
        {
            var points = s.Points.ToList();
            if (index < points.Count) points[index] = p;
            s.Points = points;
        });
    }

    // ------------------------------------------------------------------ trying it out

    private void SetTrying(bool on)
    {
        // Trying replaces the heads' current movement for a moment; put everything back afterwards.
        if (_beforeTry is { } before)
        {
            AppServices.Engine.RestoreProgrammer(before);
            _beforeTry = null;
            _tryEffect = null;
        }
        if (!on || Selected is not { } shape) return;
        var fixtures = AppServices.Engine.Fixtures;
        var movers = AppServices.Selection.Ids
            .Where(id => fixtures.TryGetValue(id, out var f) && (f.Has(ChannelType.Pan) || f.Has(ChannelType.Tilt)))
            .ToList();
        if (movers.Count == 0) return;
        var effect = new Effect { Type = EffectType.Shape, ShapeId = shape.Id, Speed = 0.25, Size = 0.5, Spread = 0, FixtureIds = movers };
        _beforeTry = AppServices.Engine.ProgrammerSnapshot();
        AppServices.Engine.AddEffect(effect);
        _tryEffect = effect.Id;
    }
}
