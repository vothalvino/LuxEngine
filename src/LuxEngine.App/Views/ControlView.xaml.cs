using System.Text.RegularExpressions;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Control;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Fixtures;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LuxEngine.App.Views;

/// <summary>Live control of the selected fixtures: dimmer, color, position, effects and other channels.</summary>
public sealed partial class ControlView : UserControl
{
    private static readonly (string Name, Color Color)[] QuickColors =
    {
        ("Red", Color.FromArgb(255, 255, 0, 0)), ("Orange", Color.FromArgb(255, 255, 110, 0)), ("Yellow", Color.FromArgb(255, 255, 220, 0)),
        ("Green", Color.FromArgb(255, 0, 255, 0)), ("Cyan", Color.FromArgb(255, 0, 255, 255)), ("Blue", Color.FromArgb(255, 0, 0, 255)),
        ("Purple", Color.FromArgb(255, 140, 0, 255)), ("Pink", Color.FromArgb(255, 255, 0, 160)), ("White", Color.FromArgb(255, 255, 255, 255)),
    };

    /// <summary>Last color picked per fixture, used as a virtual dimmer for fixtures without a dimmer channel.</summary>
    private readonly Dictionary<Guid, Color> _baseColors = new();
    private readonly DispatcherQueueTimer _meterTimer;
    private bool _syncing;
    private bool _xyDragging;

    public ControlView()
    {
        InitializeComponent();
        BuildSwatches();
        AppServices.Selection.Changed += SyncFromSelection;
        AppServices.Document.Changed += change =>
        {
            if (change.HasFlag(ShowChange.Patch)) SyncFromSelection();
        };
        Loaded += (_, _) => SyncFromSelection();
        AppServices.PresetEditor.Changed += OnPresetEditChanged;
        AppServices.Document.Changed += change =>
        {
            var editor = AppServices.PresetEditor;
            if (!editor.IsEditing) return;
            if (change == ShowChange.All) editor.Abandon();   // another show was opened
            else if (AppServices.Engine.Show.PresetRoot.FindPreset(editor.Editing!.Id) is null) editor.Cancel();
        };

        SoundSource.SelectedIndex = AppServices.Settings.AudioSource == nameof(AudioSource.Microphone) ? 1 : 0;

        _meterTimer = DispatcherQueue.CreateTimer();
        _meterTimer.Interval = TimeSpan.FromMilliseconds(50);
        _meterTimer.Tick += (_, _) =>
        {
            SoundMeter.Value = AppServices.Sound.Level;
            UpdatePresetEditInfo();
            UpdateSaveButton(SavePresetButton);
        };
        _meterTimer.Start();
    }

    private static IReadOnlyList<Guid> Selected => AppServices.Selection.Ids;

    private static List<PatchedFixture> SelectedFixtures()
    {
        var all = AppServices.Engine.Fixtures;
        return Selected.Where(all.ContainsKey).Select(id => all[id]).ToList();
    }

    // ------------------------------------------------------------------ selection → controls

    private void SyncFromSelection()
    {
        var fixtures = SelectedFixtures();
        NothingSelected.Visibility = fixtures.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionText.Text = fixtures.Count switch
        {
            0 => "No lights selected",
            1 => $"{fixtures[0].Instance.Name} · {fixtures[0].Definition.DisplayName}",
            _ => $"{fixtures.Count} lights selected: " + string.Join(", ", fixtures.Take(4).Select(f => f.Instance.Name)) + (fixtures.Count > 4 ? "…" : ""),
        };

        bool hasColor = fixtures.Any(f => f.Channels.Any(c => c.Channel.Type.IsColorMix()));
        bool hasWhite = fixtures.Any(f => f.Has(ChannelType.White));
        bool hasPosition = fixtures.Any(f => f.Has(ChannelType.Pan) || f.Has(ChannelType.Tilt));
        ColorCard.Visibility = hasColor || fixtures.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WhiteSlider.Visibility = WhiteLabel.Visibility = hasWhite ? Visibility.Visible : Visibility.Collapsed;
        PositionCard.Visibility = hasPosition ? Visibility.Visible : Visibility.Collapsed;

        _syncing = true;
        try
        {
            var first = fixtures.FirstOrDefault();
            if (first is not null)
            {
                DimmerSlider.Value = Math.Round(ReadDimmer(first) * 100);
                WhiteSlider.Value = Read(first, ChannelType.White) ?? 0;
                var pan = Read(first, ChannelType.Pan) ?? 128;
                var tilt = Read(first, ChannelType.Tilt) ?? 128;
                MoveDot(pan / 255, tilt / 255);
            }
            DimmerText.Text = $"{DimmerSlider.Value:0}%";
        }
        finally
        {
            _syncing = false;
        }

        BuildChannelRows(fixtures);
    }

    private static double? Read(PatchedFixture fixture, ChannelType type)
    {
        var ch = fixture.Channels.FirstOrDefault(c => c.Channel.Type == type);
        if (ch.Channel is null) return null;
        return AppServices.Engine.GetPlaybackValue(fixture.Instance.Id, ch.Channel.Name);
    }

    private double ReadDimmer(PatchedFixture fixture)
    {
        if (fixture.HasDimmer) return (Read(fixture, ChannelType.Intensity) ?? 0) / 255;
        var look = AppServices.Engine.GetLook(fixture.Instance.Id);
        return look.Intensity;
    }

    // ------------------------------------------------------------------ dimmer

    private void DimmerSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (DimmerText is null) return;
        DimmerText.Text = $"{e.NewValue:0}%";
        if (_syncing) return;
        SetDimmer(e.NewValue / 100);
    }

    private void SetDimmer(double level)
    {
        foreach (var fixture in SelectedFixtures())
        {
            if (fixture.HasDimmer)
            {
                AppServices.Engine.SetValue(new[] { fixture.Instance.Id }, ChannelType.Intensity, (byte)Math.Round(level * 255));
            }
            else if (fixture.Channels.Any(c => c.Channel.Type.IsColorMix()))
            {
                var color = _baseColors.TryGetValue(fixture.Instance.Id, out var c) ? c : Colors.White;
                ColorMixer.Apply(AppServices.Engine, fixture, color, level);
            }
        }
    }

    private void DimmerOff_Click(object sender, RoutedEventArgs e) => DimmerSlider.Value = 0;
    private void DimmerFull_Click(object sender, RoutedEventArgs e) => DimmerSlider.Value = 100;

    // ------------------------------------------------------------------ color

    private void BuildSwatches()
    {
        foreach (var (name, color) in QuickColors)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["SwatchButtonStyle"],
                Content = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 24, Height = 24, Fill = new SolidColorBrush(color),
                    Stroke = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)), StrokeThickness = 1,
                },
                Tag = color,
            };
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            button.Click += (s, _) => ApplyColor((Color)((Button)s).Tag, fromSwatch: true);
            Swatches.Children.Add(button);
        }
    }

    private void Picker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncing) return;
        ApplyColor(args.NewColor, fromSwatch: false);
    }

    private void ApplyColor(Color color, bool fromSwatch)
    {
        if (fromSwatch)
        {
            _syncing = true;
            Picker.Color = color;
            _syncing = false;
        }

        double level = DimmerSlider.Value / 100;
        foreach (var fixture in SelectedFixtures())
        {
            _baseColors[fixture.Instance.Id] = color;
            if (fixture.HasDimmer)
            {
                ColorMixer.Apply(AppServices.Engine, fixture, color);
                // Take the dimmer under hand control too, so cue effects don't hide the new color.
                // A dark light is turned on so the color is visible.
                double dimmer = Read(fixture, ChannelType.Intensity) ?? 0;
                if (dimmer == 0)
                {
                    dimmer = 255;
                    _syncing = true;
                    DimmerSlider.Value = 100;
                    _syncing = false;
                }
                AppServices.Engine.SetValue(new[] { fixture.Instance.Id }, ChannelType.Intensity, (byte)Math.Round(dimmer));
            }
            else
            {
                if (level == 0)
                {
                    level = 1;
                    _syncing = true;
                    DimmerSlider.Value = 100;
                    _syncing = false;
                }
                ColorMixer.Apply(AppServices.Engine, fixture, color, level);
            }
        }
    }

    private void WhiteSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        AppServices.Engine.SetValue(Selected, ChannelType.White, (byte)e.NewValue);
    }

    // ------------------------------------------------------------------ position

    private void XYPad_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _xyDragging = true;
        XYPad.CapturePointer(e.Pointer);
        SetPositionFromPointer(e);
    }

    private void XYPad_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_xyDragging) SetPositionFromPointer(e);
    }

    private void XYPad_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _xyDragging = false;
        XYPad.ReleasePointerCaptures();
    }

    private void SetPositionFromPointer(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(XYPad).Position;
        double x = Math.Clamp(p.X / XYPad.ActualWidth, 0, 1), y = Math.Clamp(p.Y / XYPad.ActualHeight, 0, 1);
        SetPosition(x, y);
    }

    private void SetPosition(double x, double y)
    {
        MoveDot(x, y);
        AppServices.Engine.SetValue(Selected, ChannelType.Pan, (byte)Math.Round(x * 255));
        AppServices.Engine.SetValue(Selected, ChannelType.Tilt, (byte)Math.Round(y * 255));
    }

    private void MoveDot(double x, double y)
    {
        Canvas.SetLeft(XYDot, x * 190 - 9);
        Canvas.SetTop(XYDot, y * 190 - 9);
        PanTiltText.Text = $"Pan {x:P0}  ·  Tilt {y:P0}";
    }

    private void CenterPosition_Click(object sender, RoutedEventArgs e) => SetPosition(0.5, 0.5);

    // ------------------------------------------------------------------ effects

    private void Effect_Click(object sender, RoutedEventArgs e)
    {
        var fixtures = SelectedFixtures();
        if (fixtures.Count == 0) return;
        var type = Enum.Parse<EffectType>((string)((FrameworkElement)sender).Tag);

        // Brightness effects need the lights to be on.
        if (type is EffectType.Chase or EffectType.Wave or EffectType.Strobe && DimmerSlider.Value == 0)
            DimmerSlider.Value = 100;

        AppServices.Engine.AddEffect(new Effect
        {
            Type = type,
            Speed = type == EffectType.Strobe ? Math.Max(SpeedSlider.Value * 4, 2) : SpeedSlider.Value,
            Size = SizeSlider.Value,
            Spread = SpreadSlider.Value,
            FixtureIds = fixtures.Select(f => f.Instance.Id).ToList(),
        });
    }

    private void StopEffects_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected.ToHashSet();
        lock (AppServices.Engine.SyncRoot)
        {
            foreach (var effect in AppServices.Engine.Programmer.Effects.Where(ef => ef.FixtureIds.Any(selected.Contains)).ToList())
                AppServices.Engine.Programmer.RemoveEffect(effect.Id);
        }
    }

    private void EffectParam_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SpeedSlider is null || SizeSlider is null || SpreadSlider is null) return;
        var selected = Selected.ToHashSet();
        lock (AppServices.Engine.SyncRoot)
        {
            foreach (var effect in AppServices.Engine.Programmer.Effects.Where(ef => ef.FixtureIds.Any(selected.Contains)))
            {
                effect.Speed = effect.Type == EffectType.Strobe ? Math.Max(SpeedSlider.Value * 4, 2) : SpeedSlider.Value;
                effect.Size = SizeSlider.Value;
                effect.Spread = SpreadSlider.Value;
            }
        }
    }

    // ------------------------------------------------------------------ sound to light

    private async void SoundSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (SoundSwitch.IsOn)
        {
            try
            {
                AppServices.Sound.Start(CurrentAudioSource());
                AppServices.Engine.SoundToLightAmount = SoundAmount.Value;
            }
            catch (Exception ex)
            {
                SoundSwitch.IsOn = false;
                await DialogHelper.ShowMessageAsync("Could not listen to audio", ex.Message);
            }
        }
        else
        {
            AppServices.Sound.Stop();
            AppServices.Engine.SoundToLightAmount = 0;
        }
    }

    private AudioSource CurrentAudioSource() =>
        (SoundSource.SelectedItem as ComboBoxItem)?.Tag as string == nameof(AudioSource.Microphone) ? AudioSource.Microphone : AudioSource.SystemSound;

    private void SoundSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AppServices.Settings.AudioSource = CurrentAudioSource().ToString();
        AppServices.Settings.Save();
        if (SoundSwitch?.IsOn == true) AppServices.Sound.Start(CurrentAudioSource());
    }

    private void SoundAmount_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SoundSwitch?.IsOn == true) AppServices.Engine.SoundToLightAmount = e.NewValue;
    }

    // ------------------------------------------------------------------ other channels

    private static readonly HashSet<ChannelType> HandledElsewhere = new()
    {
        ChannelType.Intensity, ChannelType.Pan, ChannelType.Tilt, ChannelType.PanFine, ChannelType.TiltFine,
        ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White,
    };

    private void BuildChannelRows(List<PatchedFixture> fixtures)
    {
        ChannelRows.Children.Clear();
        var types = fixtures.SelectMany(f => f.Channels.Select(c => c.Channel.Type))
            .Where(t => !HandledElsewhere.Contains(t))
            .Distinct()
            .OrderBy(t => t.Group())
            .ThenBy(t => t)
            .ToList();

        ChannelsCard.Visibility = types.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var type in types)
        {
            var owner = fixtures.First(f => f.Has(type));
            var channel = owner.Channels.First(c => c.Channel.Type == type).Channel;

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = Pretty(type), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTipService.SetToolTip(label, channel.Name);
            var slider = new Slider { Minimum = 0, Maximum = 255, Value = Read(owner, type) ?? channel.Default, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, e) =>
            {
                if (!_syncing) AppServices.Engine.SetValue(Selected, type, (byte)e.NewValue);
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(label);
            row.Children.Add(slider);

            if (channel.Capabilities.Count > 1)
            {
                var menu = new MenuFlyout();
                foreach (var cap in channel.Capabilities.Take(60))
                {
                    var item = new MenuFlyoutItem { Text = $"{cap.Label}  ({cap.From}–{cap.To})" };
                    var label2 = cap.Label;
                    item.Click += (_, _) =>
                    {
                        byte value = SetCapability(type, label2);
                        _syncing = true;
                        slider.Value = value;
                        _syncing = false;
                    };
                    menu.Items.Add(item);
                }
                var drop = new DropDownButton { Content = new FontIcon { Glyph = "", FontSize = 14 }, Flyout = menu, Padding = new Thickness(6, 4, 6, 4) };
                ToolTipService.SetToolTip(drop, "Choose a setting");
                Grid.SetColumn(drop, 2);
                row.Children.Add(drop);
            }

            ChannelRows.Children.Add(row);
        }
    }

    /// <summary>Sets each selected fixture to the middle of the range with the given label.</summary>
    private static byte SetCapability(ChannelType type, string label)
    {
        byte last = 0;
        foreach (var fixture in SelectedFixtures())
        foreach (var ch in fixture.Channels.Where(c => c.Channel.Type == type))
        {
            var cap = ch.Channel.Capabilities.FirstOrDefault(c => c.Label == label);
            if (cap is null) continue;
            last = (byte)((cap.From + cap.To) / 2);
            AppServices.Engine.SetValue(fixture.Instance.Id, ch.Channel.Name, last);
        }
        return last;
    }

    private static string Pretty(ChannelType type)
    {
        var words = Regex.Replace(type.ToString(), "(?<!^)([A-Z])", " $1").ToLowerInvariant();
        return type == ChannelType.UV ? "UV" : char.ToUpperInvariant(words[0]) + words[1..];
    }

    // ------------------------------------------------------------------ actions

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Engine.ClearProgrammer();
        _baseColors.Clear();
        SyncFromSelection();
    }

    // ------------------------------------------------------------------ preset editing

    private void OnPresetEditChanged()
    {
        var preset = AppServices.PresetEditor.Editing;
        PresetEditBar.Visibility = preset is null ? Visibility.Collapsed : Visibility.Visible;
        _baseColors.Clear();
        SyncFromSelection();
        if (preset is null) return;
        PresetEditTitle.Text = $"Editing preset \"{preset.Name}\"";
        UpdatePresetEditInfo();
    }

    private void UpdatePresetEditInfo()
    {
        if (!AppServices.PresetEditor.IsEditing) return;
        int count = AppServices.Engine.ProgrammerFixtures().Count;
        PresetEditInfo.Text = $"{count} light{(count == 1 ? "" : "s")} in this preset (marked with a ring). The controls show the preset's values; change what you need, then save.";
    }

    /// <summary>"Save as preset" only makes sense when something has been set (and not while editing a preset).</summary>
    internal static void UpdateSaveButton(Button button)
    {
        bool editing = AppServices.PresetEditor.IsEditing;
        bool enabled = !editing && !AppServices.Engine.Programmer.IsEmpty;
        if (button.IsEnabled == enabled) return;
        button.IsEnabled = enabled;
        ToolTipService.SetToolTip(button, editing ? "Finish editing the preset first"
            : enabled ? "Save the current look as a new preset"
            : "Nothing to save yet: select lights and set a color, dimmer or position first");
    }

    private void PresetEditSave_Click(object sender, RoutedEventArgs e) => AppServices.PresetEditor.Save();

    private void PresetEditCancel_Click(object sender, RoutedEventArgs e) => AppServices.PresetEditor.Cancel();

    private async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.MainWindow is not null) await AppServices.MainWindow.Presets.RecordPresetAsync();
    }
}
