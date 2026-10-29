using LuxEngine.App.Services;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Fixtures;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace LuxEngine.App.Views;

/// <summary>
/// The bar that opens with Shift + a letter (e.g. Shift+D for dimmer) while lights are selected.
/// Arrow keys, the mouse wheel or a typed number then change that parameter on the selected lights.
/// </summary>
public sealed class ParameterBar : UserControl
{
    public enum Kind { Dimmer, Channel, Position, Speed, Coldness }

    public sealed record Parameter(VirtualKey Key, string Name, Kind Kind, params ChannelType[] Types)
    {
        public string Keys => $"Shift+{Key}";
    }

    public static readonly IReadOnlyList<Parameter> Parameters = new[]
    {
        new Parameter(VirtualKey.D, "Dimmer", Kind.Dimmer),
        new Parameter(VirtualKey.Z, "Zoom", Kind.Channel, ChannelType.Zoom),
        new Parameter(VirtualKey.F, "Focus", Kind.Channel, ChannelType.Focus),
        new Parameter(VirtualKey.I, "Iris", Kind.Channel, ChannelType.Iris),
        new Parameter(VirtualKey.X, "Frost", Kind.Channel, ChannelType.Frost),
        new Parameter(VirtualKey.G, "Gobo", Kind.Channel, ChannelType.Gobo),
        new Parameter(VirtualKey.R, "Gobo rotation", Kind.Channel, ChannelType.GoboRotation),
        new Parameter(VirtualKey.C, "Colour wheel", Kind.Channel, ChannelType.ColorWheel),
        new Parameter(VirtualKey.M, "Prism", Kind.Channel, ChannelType.Prism),
        new Parameter(VirtualKey.S, "Strobe", Kind.Channel, ChannelType.Strobe, ChannelType.Shutter),
        new Parameter(VirtualKey.W, "White", Kind.Channel, ChannelType.White),
        new Parameter(VirtualKey.A, "Amber", Kind.Channel, ChannelType.Amber),
        new Parameter(VirtualKey.U, "UV", Kind.Channel, ChannelType.UV),
        new Parameter(VirtualKey.K, "Warm / cold", Kind.Coldness, ChannelType.ColorTemperature),
        new Parameter(VirtualKey.P, "Position", Kind.Position, ChannelType.Pan, ChannelType.Tilt),
        new Parameter(VirtualKey.V, "Effect speed", Kind.Speed),
    };

    private readonly TextBlock _keys = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _value = new() { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _typed = new() { FontSize = 16, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Colors.Orange), VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 1, Width = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private Parameter? _active;
    private ChannelType _type;
    private string _input = "";
    private DateTime _messageUntil;

    public ParameterBar()
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        top.Children.Add(_keys);
        top.Children.Add(_name);
        top.Children.Add(_value);
        top.Children.Add(_level);
        top.Children.Add(_typed);
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(top);
        panel.Children.Add(_hint);
        Content = new Border
        {
            Child = panel,
            Padding = new Thickness(16, 10, 16, 10),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 24, 24, 30)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 61)),
            BorderThickness = new Thickness(1.5),
            RequestedTheme = ElementTheme.Dark,
        };
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;

        AppServices.Selection.Changed += () =>
        {
            if (AppServices.Selection.Count == 0) Close();
            else Refresh();
        };
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(120);
        _timer.Tick += (_, _) =>
        {
            if (_active is not null) Refresh();
            else if (Visibility == Visibility.Visible && DateTime.Now > _messageUntil) Visibility = Visibility.Collapsed;
        };
        _timer.Start();
    }

    public bool IsOpen => _active is not null;

    /// <summary>Called after the bar changed something, so the Control tab can show the new values.</summary>
    public event Action? ValuesChanged;

    /// <summary>Reads and sets the dimmer through the Control tab, which knows how to dim lights without a dimmer channel.</summary>
    public Func<double>? GetDimmerPercent { get; set; }
    public Action<double>? SetDimmerPercent { get; set; }

    private static IReadOnlyList<Guid> Selected => AppServices.Selection.Ids;

    /// <summary>Opens the parameter for this key. Does nothing (returns false) when no lights are selected.</summary>
    public bool TryOpen(VirtualKey key)
    {
        var parameter = Parameters.FirstOrDefault(p => p.Key == key);
        if (parameter is null || Selected.Count == 0) return false;

        var engine = AppServices.Engine;
        bool supported;
        switch (parameter.Kind)
        {
            case Kind.Dimmer:
                supported = true;
                break;
            case Kind.Coldness:
                supported = engine.AnyHas(Selected, ChannelType.ColorTemperature) || engine.GetColdness(Selected) is not null;
                _type = ChannelType.ColorTemperature;
                break;
            case Kind.Speed:
                supported = RunningEffects().Count > 0;
                break;
            default:
                var type = parameter.Types.Cast<ChannelType?>().FirstOrDefault(t => engine.AnyHas(Selected, t!.Value));
                supported = type is not null;
                if (type is ChannelType t2) _type = t2;
                break;
        }
        if (!supported)
        {
            ShowMessage(parameter.Kind == Kind.Speed
                ? "No effect or movement is running on the selected lights"
                : $"None of the selected lights has {parameter.Name}");
            return true;
        }

        _active = parameter;
        _input = "";
        Visibility = Visibility.Visible;
        Refresh();
        return true;
    }

    public void Close()
    {
        _active = null;
        _input = "";
        Visibility = Visibility.Collapsed;
    }

    public void ShowMessage(string text)
    {
        _active = null;
        _keys.Text = "";
        _name.Text = text;
        _value.Text = "";
        _typed.Text = "";
        _level.Visibility = Visibility.Collapsed;
        _hint.Text = "";
        _messageUntil = DateTime.Now.AddSeconds(2.5);
        Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ keys

    /// <summary>Handles a key while the bar is open. Returns false for keys it does not use.</summary>
    public bool HandleKey(VirtualKey key, bool ctrl)
    {
        if (_active is null) return false;
        int digit = key switch
        {
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => key - VirtualKey.Number0,
            >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => key - VirtualKey.NumberPad0,
            _ => -1,
        };
        if (digit >= 0)
        {
            if (_input.Length < 3) _input += digit;
            Refresh();
            return true;
        }
        switch (key)
        {
            case VirtualKey.Escape: Close(); return true;
            case VirtualKey.Back:
                if (_input.Length > 0) _input = _input[..^1];
                Refresh();
                return true;
            case VirtualKey.Enter:
                if (_input.Length > 0) ApplyTyped(int.Parse(_input), dmx: ctrl);
                _input = "";
                break;
            case VirtualKey.Up: Adjust(ctrl ? 1 : 13, vertical: true); break;
            case VirtualKey.Down: Adjust(ctrl ? -1 : -13, vertical: true); break;
            case VirtualKey.PageUp: Adjust(64, vertical: true); break;
            case VirtualKey.PageDown: Adjust(-64, vertical: true); break;
            case VirtualKey.Right: Step(+1, ctrl); break;
            case VirtualKey.Left: Step(-1, ctrl); break;
            case VirtualKey.Home: SetAll(255); break;
            case VirtualKey.End: SetAll(0); break;
            default: return false;
        }
        Changed();
        return true;
    }

    /// <summary>Mouse wheel while the bar is open: one notch is one step up or down.</summary>
    public void Wheel(int delta)
    {
        if (_active is null) return;
        Adjust(delta > 0 ? 13 : -13, vertical: true);
        Changed();
    }

    private void Changed()
    {
        Refresh();
        ValuesChanged?.Invoke();
    }

    /// <summary>Changes the value by <paramref name="dmx"/> steps (13 ≈ 5%).</summary>
    private void Adjust(int dmx, bool vertical)
    {
        var engine = AppServices.Engine;
        switch (_active!.Kind)
        {
            case Kind.Dimmer:
                SetDimmerPercent?.Invoke(Math.Clamp((GetDimmerPercent?.Invoke() ?? 0) + Math.Sign(dmx) * Math.Max(1, Math.Abs(dmx) * 100 / 255.0), 0, 100));
                break;
            case Kind.Position:
                // Up arrow aims higher (lower tilt values, like the top of the XY pad).
                engine.NudgeType(Selected, vertical ? ChannelType.Tilt : ChannelType.Pan, vertical ? -dmx : dmx);
                break;
            case Kind.Speed:
                foreach (var effect in RunningEffects())
                    AppServices.Engine.UpdateEffect(effect.Id, e => e.Speed = Math.Clamp(e.Speed * Math.Pow(Math.Abs(dmx) <= 1 ? 1.05 : 1.25, Math.Sign(dmx)), 0.02, 20));
                break;
            case Kind.Coldness when !engine.AnyHas(Selected, ChannelType.ColorTemperature):
                engine.SetColdness(Selected, (engine.GetColdness(Selected) ?? 0.5) + dmx / 255.0);
                break;
            default:
                engine.NudgeType(Selected, _type, dmx);
                break;
        }
    }

    /// <summary>Left/right: pan for position, the next/previous named range (e.g. gobo) when there is one, otherwise a small step.</summary>
    private void Step(int direction, bool ctrl)
    {
        var engine = AppServices.Engine;
        if (_active!.Kind == Kind.Position)
        {
            Adjust(direction * (ctrl ? 1 : 13), vertical: false);
            return;
        }
        if (_active.Kind == Kind.Channel && engine.StepRange(Selected, _type, direction) is not null) return;
        Adjust(direction * (ctrl ? 1 : 13), vertical: true);
    }

    private void SetAll(byte value)
    {
        var engine = AppServices.Engine;
        switch (_active!.Kind)
        {
            case Kind.Dimmer: SetDimmerPercent?.Invoke(value == 0 ? 0 : 100); break;
            case Kind.Position:
                engine.SetValue(Selected, ChannelType.Pan, 128);
                engine.SetValue(Selected, ChannelType.Tilt, 128);
                break;
            case Kind.Speed: break;
            case Kind.Coldness when !engine.AnyHas(Selected, ChannelType.ColorTemperature):
                engine.SetColdness(Selected, value == 0 ? 0 : 1);
                break;
            default: engine.SetValue(Selected, _type, value); break;
        }
    }

    /// <summary>A typed number: a percentage, or a DMX value (0–255) when Ctrl is held for Enter.</summary>
    private void ApplyTyped(int number, bool dmx)
    {
        double percent = dmx ? Math.Clamp(number, 0, 255) / 2.55 : Math.Clamp(number, 0, 100);
        byte value = (byte)Math.Round(dmx ? Math.Clamp(number, 0, 255) : percent * 2.55);
        var engine = AppServices.Engine;
        switch (_active!.Kind)
        {
            case Kind.Dimmer: SetDimmerPercent?.Invoke(percent); break;
            case Kind.Position: break;   // two values: use the arrow keys
            case Kind.Speed: break;
            case Kind.Coldness when !engine.AnyHas(Selected, ChannelType.ColorTemperature):
                engine.SetColdness(Selected, percent / 100);
                break;
            default: engine.SetValue(Selected, _type, value); break;
        }
    }

    private static List<Effect> RunningEffects()
    {
        var selected = Selected.ToHashSet();
        return AppServices.Engine.ProgrammerSnapshot().Effects.Where(e => e.FixtureIds.Any(selected.Contains)).ToList();
    }

    // ------------------------------------------------------------------ display

    private void Refresh()
    {
        if (_active is not { } p) return;
        var engine = AppServices.Engine;
        _keys.Text = p.Keys;
        _name.Text = p.Name;
        _typed.Text = _input.Length > 0 ? $"{_input}_" : "";
        _level.Visibility = Visibility.Visible;
        string hint = "↑↓ or wheel ±5%  ·  Ctrl+↑↓ fine  ·  PgUp/PgDn ±25%  ·  Home full  ·  End zero  ·  type a number + Enter (%; Ctrl+Enter = DMX)  ·  Esc close";

        switch (p.Kind)
        {
            case Kind.Dimmer:
                double d = GetDimmerPercent?.Invoke() ?? 0;
                _value.Text = $"{d:0}%";
                _level.Value = d / 100;
                break;
            case Kind.Position:
                double pan = engine.GetTypeValue(Selected, ChannelType.Pan) ?? 128, tilt = engine.GetTypeValue(Selected, ChannelType.Tilt) ?? 128;
                _value.Text = $"Pan {pan / 2.55:0}%  ·  Tilt {tilt / 2.55:0}%";
                _level.Visibility = Visibility.Collapsed;
                hint = "← → pan  ·  ↑ ↓ tilt  ·  Ctrl for fine steps  ·  Home centre  ·  Esc close";
                break;
            case Kind.Speed:
                var effects = RunningEffects();
                _value.Text = effects.Count == 0 ? "—" : $"{effects[0].Speed:0.00} per second";
                _level.Visibility = Visibility.Collapsed;
                hint = "↑ faster  ·  ↓ slower  ·  Ctrl for small steps  ·  Esc close";
                break;
            case Kind.Coldness when !engine.AnyHas(Selected, ChannelType.ColorTemperature):
                double c = engine.GetColdness(Selected) ?? 0.5;
                _value.Text = c < 0.4 ? "Warm" : c > 0.6 ? "Cold" : "Neutral";
                _level.Value = c;
                hint = "↑ colder  ·  ↓ warmer  ·  Home all cold  ·  End all warm  ·  type 0–100 + Enter  ·  Esc close";
                break;
            default:
                double v = engine.GetTypeValue(Selected, _type) ?? 0;
                var range = engine.RangeName(Selected, _type);
                _value.Text = range is null ? $"{v / 2.55:0}%" : $"{range}";
                _level.Value = v / 255;
                if (range is not null) hint = "← → previous/next setting  ·  ↑↓ fine-tune  ·  Home/End  ·  type a number + Enter  ·  Esc close";
                break;
        }
        _hint.Text = hint;
    }
}
