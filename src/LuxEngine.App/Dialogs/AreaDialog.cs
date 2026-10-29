using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Engine;
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
/// Areas such as "Wall" or "Stage floor": for every moving head a pan/tilt box that movements in
/// that area stay inside. Heads can be aimed live from here to find the corners.
/// </summary>
public sealed class AreaDialog : ContentDialog
{
    private const double PadSize = 256;

    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Width = 180, Height = 260 };
    private readonly ComboBox _head = new() { Header = "Moving head", Width = 240 };
    private readonly Canvas _pad = new() { Width = PadSize, Height = PadSize, Background = (Brush)Application.Current.Resources["LuxStageBrush"] };
    private readonly RadioButton _aimMode = new() { Content = "Drag to aim the light", IsChecked = true, GroupName = "areaMode" };
    private readonly RadioButton _boxMode = new() { Content = "Drag to draw the box", GroupName = "areaMode" };
    private readonly NumberBox _panFrom = Box("Pan from"), _panTo = Box("Pan to"), _tiltFrom = Box("Tilt from"), _tiltTo = Box("Tilt to");
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Width = 520, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
    private readonly ToggleButton _test = new() { Content = "Test: circle inside the box" };
    private readonly Panel _editor;
    private readonly ProgrammerSnapshot _before;
    private readonly List<PatchedFixture> _heads;
    private Point? _boxStart;
    private bool _dragging;
    private bool _loading;
    private Guid? _testEffect;

    private AreaDialog()
    {
        Title = "Movement areas";
        CloseButtonText = "Done";
        _before = AppServices.Engine.ProgrammerSnapshot();
        _heads = AppServices.Engine.Show.Fixtures
            .Select(f => AppServices.Engine.Fixtures.GetValueOrDefault(f.Id))
            .OfType<PatchedFixture>()
            .Where(f => f.Has(ChannelType.Pan) || f.Has(ChannelType.Tilt))
            .ToList();

        var add = new Button { Content = "New area" };
        add.Click += (_, _) => AddArea();
        var rename = new Button { Content = "Rename" };
        rename.Click += (_, _) => Rename();
        var delete = new Button { Content = "Delete" };
        delete.Click += (_, _) => DeleteArea();
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        listButtons.Children.Add(rename);
        listButtons.Children.Add(delete);
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(add);
        left.Children.Add(_list);
        left.Children.Add(listButtons);

        foreach (var h in _heads) _head.Items.Add(h.Instance.Name);
        var firstSelected = _heads.FindIndex(h => AppServices.Selection.Contains(h.Instance.Id));
        _head.SelectedIndex = _heads.Count == 0 ? -1 : Math.Max(0, firstSelected);

        var corner1 = new Button { Content = "Aim → corner 1" };
        ToolTipService.SetToolTip(corner1, "Use where this light points now as one corner of its box");
        corner1.Click += (_, _) => SetCorner(first: true);
        var corner2 = new Button { Content = "Aim → corner 2" };
        ToolTipService.SetToolTip(corner2, "Use where this light points now as the opposite corner");
        corner2.Click += (_, _) => SetCorner(first: false);
        var copyAll = new Button { Content = "Copy box to all heads" };
        ToolTipService.SetToolTip(copyAll, "Same box for every moving head (for identical heads hung side by side)");
        copyAll.Click += (_, _) => CopyToAll();
        var removeHead = new Button { Content = "Remove this head" };
        ToolTipService.SetToolTip(removeHead, "This head will not move in this area");
        removeHead.Click += (_, _) => EditArea(a => a.Bounds.Remove(Head!.Instance.Id));
        _test.Click += (_, _) => UpdateTest();

        var numbers = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        numbers.ColumnDefinitions.Add(new ColumnDefinition());
        numbers.RowDefinitions.Add(new RowDefinition());
        numbers.RowDefinitions.Add(new RowDefinition());
        Grid.SetColumn(_panTo, 1);
        Grid.SetRow(_tiltFrom, 1);
        Grid.SetRow(_tiltTo, 1);
        Grid.SetColumn(_tiltTo, 1);
        numbers.Children.Add(_panFrom);
        numbers.Children.Add(_panTo);
        numbers.Children.Add(_tiltFrom);
        numbers.Children.Add(_tiltTo);
        foreach (var box in new[] { _panFrom, _panTo, _tiltFrom, _tiltTo }) box.ValueChanged += (_, _) => NumbersChanged();

        var side = new StackPanel { Spacing = 8, Width = 250 };
        side.Children.Add(_head);
        side.Children.Add(_aimMode);
        side.Children.Add(_boxMode);
        var corners = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        corners.Children.Add(corner1);
        corners.Children.Add(corner2);
        side.Children.Add(corners);
        side.Children.Add(numbers);
        side.Children.Add(copyAll);
        side.Children.Add(removeHead);
        side.Children.Add(_test);

        var padAndSide = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        padAndSide.Children.Add(new Border { Child = _pad, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top });
        padAndSide.Children.Add(side);
        var editor = new StackPanel { Spacing = 8 };
        editor.Children.Add(padAndSide);
        editor.Children.Add(_status);
        _editor = editor;

        var layout = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        layout.Children.Add(left);
        layout.Children.Add(editor);
        Content = layout;

        _list.SelectionChanged += (_, _) => Load();
        _head.SelectionChanged += (_, _) => Load();
        _pad.PointerPressed += Pad_PointerPressed;
        _pad.PointerMoved += Pad_PointerMoved;
        _pad.PointerReleased += (_, e) => { _dragging = false; _boxStart = null; _pad.ReleasePointerCapture(e.Pointer); };
        Closed += (_, _) => AppServices.Engine.RestoreProgrammer(_before);   // aiming here was only for finding the corners

        RefreshList(Areas.FirstOrDefault()?.Id);
    }

    public static async Task ShowAreasAsync() => await DialogHelper.ShowAsync(new AreaDialog());

    private static List<MovementArea> Areas => AppServices.Engine.Show.Areas;

    private MovementArea? Area => _list.SelectedIndex >= 0 && _list.SelectedIndex < Areas.Count ? Areas[_list.SelectedIndex] : null;

    private PatchedFixture? Head => _head.SelectedIndex >= 0 && _head.SelectedIndex < _heads.Count ? _heads[_head.SelectedIndex] : null;

    private static NumberBox Box(string header) => new()
    {
        Header = header, Minimum = 0, Maximum = 255, Width = 118,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
    };

    // ------------------------------------------------------------------ list

    private void RefreshList(Guid? select)
    {
        _list.Items.Clear();
        foreach (var a in Areas) _list.Items.Add(a.Name);
        _list.SelectedIndex = Areas.FindIndex(a => a.Id == select);
        if (_list.SelectedIndex < 0 && Areas.Count > 0) _list.SelectedIndex = 0;
        Load();
    }

    private void AddArea()
    {
        var names = Areas.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = new[] { "Stage", "Wall", "Audience", "Floor", "Ceiling" }.FirstOrDefault(n => !names.Contains(n)) ?? $"Area {Areas.Count + 1}";
        var area = new MovementArea { Name = name };
        AppServices.Engine.Edit(show => show.Areas.Add(area));
        AppServices.Document.MarkDirty();
        RefreshList(area.Id);
    }

    private void Rename()
    {
        if (Area is not { } area) return;
        var box = new TextBox { Text = area.Name, Width = 200 };
        var ok = new Button { Content = "Rename", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(ok);
        var flyout = new Flyout { Content = panel };
        ok.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            if (name.Length > 0) EditArea(a => a.Name = name);
            flyout.Hide();
            RefreshList(area.Id);
        };
        flyout.ShowAt(_list);
    }

    private void DeleteArea()
    {
        if (Area is not { } area) return;
        // Movements that used it move around the current aim again (see EffectEngine).
        AppServices.Engine.Edit(show => show.Areas.Remove(area));
        AppServices.Document.MarkDirty();
        RefreshList(null);
    }

    private void EditArea(Action<MovementArea> change)
    {
        if (Area is not { } area) return;
        AppServices.Engine.Edit(_ => change(area));
        AppServices.Document.MarkDirty();
        Load();
    }

    // ------------------------------------------------------------------ one head's box

    private AreaBounds? Bounds => Area is { } a && Head is { } h ? a.Bounds.GetValueOrDefault(h.Instance.Id) : null;

    private void Load()
    {
        _editor.Visibility = Area is null ? Visibility.Collapsed : Visibility.Visible;
        if (Area is null) return;
        if (Head is null)
        {
            _status.Text = "There are no moving heads in this show yet.";
            return;
        }
        _loading = true;
        var b = Bounds;
        _panFrom.Value = b?.PanMin ?? double.NaN;
        _panTo.Value = b?.PanMax ?? double.NaN;
        _tiltFrom.Value = b?.TiltMin ?? double.NaN;
        _tiltTo.Value = b?.TiltMax ?? double.NaN;
        _loading = false;

        int done = Area!.Bounds.Keys.Count(id => _heads.Any(h => h.Instance.Id == id));
        _status.Text = (b is null
            ? $"{Head.Instance.Name} has no box in \"{Area.Name}\" yet: aim it at one corner of the area and press \"Aim → corner 1\", then the opposite corner and \"Aim → corner 2\". "
            : "")
            + $"{done} of {_heads.Count} moving heads have a box in \"{Area.Name}\". Movements in this area are centred in each head's box and never leave it.";
        Draw();
        UpdateTest();
    }

    private (double Pan, double Tilt) Aim()
    {
        var id = Head!.Instance.Id;
        string? Channel(ChannelType t) => Head.Channels.FirstOrDefault(c => c.Channel.Type == t).Channel?.Name;
        double pan = Channel(ChannelType.Pan) is { } p ? AppServices.Engine.GetPlaybackValue(id, p) ?? 128 : 128;
        double tilt = Channel(ChannelType.Tilt) is { } t ? AppServices.Engine.GetPlaybackValue(id, t) ?? 128 : 128;
        return (pan, tilt);
    }

    private void SetAim(double pan, double tilt)
    {
        foreach (var ch in Head!.Channels)
        {
            if (ch.Channel.Type == ChannelType.Pan) AppServices.Engine.SetValue(Head.Instance.Id, ch.Channel.Name, (byte)Math.Round(pan));
            if (ch.Channel.Type == ChannelType.Tilt) AppServices.Engine.SetValue(Head.Instance.Id, ch.Channel.Name, (byte)Math.Round(tilt));
        }
    }

    private void SetBounds(Func<AreaBounds, AreaBounds> change)
    {
        if (Head is not { } head) return;
        EditArea(a => a.Bounds[head.Instance.Id] = change(a.Bounds.GetValueOrDefault(head.Instance.Id) ?? new AreaBounds()));
    }

    private void SetCorner(bool first)
    {
        if (Head is null) return;
        var (pan, tilt) = Aim();
        int p = (int)Math.Round(pan), t = (int)Math.Round(tilt);
        SetBounds(b =>
        {
            // A head without a box yet starts as a point at the first corner it gets.
            bool fresh = Bounds is null;
            return first
                ? new AreaBounds { PanMin = p, TiltMin = t, PanMax = fresh ? p : b.PanMax, TiltMax = fresh ? t : b.TiltMax }
                : new AreaBounds { PanMax = p, TiltMax = t, PanMin = fresh ? p : b.PanMin, TiltMin = fresh ? t : b.TiltMin };
        });
    }

    private void NumbersChanged()
    {
        if (_loading || Head is null) return;
        static int V(NumberBox box, int fallback) => double.IsNaN(box.Value) ? fallback : (int)box.Value;
        SetBounds(b => new AreaBounds
        {
            PanMin = V(_panFrom, b.PanMin), PanMax = V(_panTo, b.PanMax),
            TiltMin = V(_tiltFrom, b.TiltMin), TiltMax = V(_tiltTo, b.TiltMax),
        });
    }

    private void CopyToAll()
    {
        if (Bounds is not { } b) return;
        EditArea(a =>
        {
            foreach (var h in _heads)
                a.Bounds[h.Instance.Id] = new AreaBounds { PanMin = b.PanMin, PanMax = b.PanMax, TiltMin = b.TiltMin, TiltMax = b.TiltMax };
        });
    }

    // ------------------------------------------------------------------ the pad

    private static double ToPad(double dmx) => dmx / 255 * PadSize;
    private static double FromPad(double px) => Math.Clamp(px / PadSize * 255, 0, 255);

    private void Draw()
    {
        _pad.Children.Clear();
        var grid = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255));
        _pad.Children.Add(new Line { X1 = PadSize / 2, X2 = PadSize / 2, Y2 = PadSize, Stroke = grid });
        _pad.Children.Add(new Line { Y1 = PadSize / 2, Y2 = PadSize / 2, X2 = PadSize, Stroke = grid });
        if (Head is null) return;

        // Other heads' boxes faintly, this head's box clearly.
        foreach (var (id, other) in Area!.Bounds)
            if (id != Head.Instance.Id) AddBox(other.Normalized(), 30);
        if (Bounds is { } b) AddBox(b.Normalized(), 90);

        var (pan, tilt) = Aim();
        var dot = new Ellipse { Width = 18, Height = 18, Fill = (Brush)Application.Current.Resources["LuxAccentBrush"], Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 2, IsHitTestVisible = false };
        Canvas.SetLeft(dot, ToPad(pan) - 9);
        Canvas.SetTop(dot, ToPad(tilt) - 9);
        _pad.Children.Add(dot);

        void AddBox(AreaBounds box, byte alpha)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(2, ToPad(box.PanMax) - ToPad(box.PanMin)),
                Height = Math.Max(2, ToPad(box.TiltMax) - ToPad(box.TiltMin)),
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, 255, 200, 61)),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)Math.Min(255, alpha * 2.5), 255, 200, 61)),
                StrokeThickness = 1.5, IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, ToPad(box.PanMin));
            Canvas.SetTop(rect, ToPad(box.TiltMin));
            _pad.Children.Add(rect);
        }
    }

    private void Pad_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Head is null || Area is null) return;
        var p = e.GetCurrentPoint(_pad).Position;
        _pad.CapturePointer(e.Pointer);
        _dragging = true;
        if (_boxMode.IsChecked == true) _boxStart = p;
        else
        {
            SetAim(FromPad(p.X), FromPad(p.Y));
            Draw();
        }
        e.Handled = true;
    }

    private void Pad_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || Head is null) return;
        var p = e.GetCurrentPoint(_pad).Position;
        if (_boxStart is { } start)
        {
            SetBounds(_ => new AreaBounds
            {
                PanMin = (int)FromPad(Math.Min(start.X, p.X)), PanMax = (int)FromPad(Math.Max(start.X, p.X)),
                TiltMin = (int)FromPad(Math.Min(start.Y, p.Y)), TiltMax = (int)FromPad(Math.Max(start.Y, p.Y)),
            });
        }
        else
        {
            SetAim(FromPad(p.X), FromPad(p.Y));
            Draw();
        }
    }

    // ------------------------------------------------------------------ testing

    /// <summary>Runs a circle that touches the edges of the box, on the head being edited.</summary>
    private void UpdateTest()
    {
        if (_testEffect is Guid running)
        {
            AppServices.Engine.RemoveEffect(running);
            _testEffect = null;
        }
        if (_test.IsChecked != true || Head is null || Area is null || Bounds is null) return;
        var effect = new Effect { Type = EffectType.Circle, AreaId = Area.Id, Speed = 0.3, Size = 1, Spread = 0, FixtureIds = { Head.Instance.Id } };
        AppServices.Engine.AddEffect(effect);
        _testEffect = effect.Id;
    }
}
