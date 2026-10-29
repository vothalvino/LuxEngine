using System.Text.RegularExpressions;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Control;
using LuxEngine.Core.Effects;
using LuxEngine.Core.Engine;
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
            if (change == ShowChange.All) FillMovementLists();
        };
        Loaded += (_, _) =>
        {
            FillMovementLists();
            SyncFromSelection();
        };
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
            UpdatePresetValues();
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
        NothingSelected.Visibility = fixtures.Count == 0 && !AppServices.PresetEditor.IsEditing ? Visibility.Visible : Visibility.Collapsed;
        SelectionText.Text = fixtures.Count switch
        {
            0 => "No lights selected",
            1 => $"{fixtures[0].Instance.Name} · {fixtures[0].Definition.DisplayName}",
            _ => $"{fixtures.Count} lights selected: " + string.Join(", ", fixtures.Take(4).Select(f => f.Instance.Name)) + (fixtures.Count > 4 ? "…" : ""),
        };

        // The color wheel only makes sense for lights that can mix colors (RGB or CMY).
        // Warm/cold white lights get plain sliders under "More controls" instead.
        bool hasColor = fixtures.Any(MixesColor);
        bool hasWhite = fixtures.Any(f => f.Has(ChannelType.White));
        bool hasPosition = fixtures.Any(f => f.Has(ChannelType.Pan) || f.Has(ChannelType.Tilt));
        ColorCard.Visibility = hasColor ? Visibility.Visible : Visibility.Collapsed;
        // White-only lights have their main controls in "More controls", so put that card next to the dimmer.
        // Moving heads keep Position and Movement first.
        Cards.Children.Remove(ChannelsCard);
        bool whiteOnly = !hasColor && !hasPosition && fixtures.Count > 0;
        Cards.Children.Insert(whiteOnly ? Cards.Children.IndexOf(ColorCard) : Cards.Children.Count, ChannelsCard);
        WhiteSlider.Visibility = WhiteLabel.Visibility = hasWhite ? Visibility.Visible : Visibility.Collapsed;
        PositionCard.Visibility = hasPosition ? Visibility.Visible : Visibility.Collapsed;
        MovementCard.Visibility = hasPosition ? Visibility.Visible : Visibility.Collapsed;

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

        BuildChannelRows(fixtures, colorCardShown: hasColor);
    }

    private static bool MixesColor(PatchedFixture f) =>
        f.Has(ChannelType.Red) || f.Has(ChannelType.Green) || f.Has(ChannelType.Blue) ||
        f.Has(ChannelType.Cyan) || f.Has(ChannelType.Magenta) || f.Has(ChannelType.Yellow);

    private static double? Read(PatchedFixture fixture, ChannelType type)
    {
        var ch = fixture.Channels.FirstOrDefault(c => c.Channel.Type == type);
        if (ch.Channel is null) return null;
        return AppServices.Engine.GetPlaybackValue(fixture.Instance.Id, ch.Channel.Name);
    }

    /// <summary>How bright a light without a dimmer is set to: its strongest color channel, before the master and effects.</summary>
    private static double ColorBrightness(PatchedFixture fixture) =>
        fixture.Channels.Where(c => c.Channel.Type.IsAdditiveColor())
            .Select(c => AppServices.Engine.GetPlaybackValue(fixture.Instance.Id, c.Channel.Name) ?? 0)
            .DefaultIfEmpty(0).Max() / 255;

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
            else if (!MixesColor(fixture) && fixture.Channels.Any(c => c.Channel.Type.IsAdditiveColor()))
            {
                ScaleWhites(fixture, level);
                RefreshChannelSliders();
            }
            else if (fixture.Channels.Any(c => c.Channel.Type.IsColorMix()))
            {
                var color = _baseColors.TryGetValue(fixture.Instance.Id, out var c) ? c : Colors.White;
                ColorMixer.Apply(AppServices.Engine, fixture, color, level);
            }
        }
    }

    /// <summary>
    /// Brightness for white-only lights without a dimmer (e.g. warm/cold white): scales their channels
    /// together so the warm/cold balance stays the same. From dark, all channels come up equally.
    /// </summary>
    private static void ScaleWhites(PatchedFixture fixture, double level)
    {
        var channels = fixture.Channels.Where(c => c.Channel.Type.IsAdditiveColor()).ToList();
        var current = channels.Select(c => AppServices.Engine.GetPlaybackValue(fixture.Instance.Id, c.Channel.Name) ?? 0).ToList();
        double max = current.DefaultIfEmpty(0).Max();
        for (int i = 0; i < channels.Count; i++)
        {
            double share = max > 0 ? current[i] / max : 1;
            AppServices.Engine.SetValue(fixture.Instance.Id, channels[i].Channel.Name, (byte)Math.Round(Math.Clamp(share * level, 0, 1) * 255));
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

        foreach (var fixture in SelectedFixtures())
        {
            _baseColors[fixture.Instance.Id] = color;
            // Only the color channels change; the dimmer stays where it is, so a dark light stays dark.
            // Lights without a dimmer mix the color at their current brightness.
            if (fixture.HasDimmer) ColorMixer.Apply(AppServices.Engine, fixture, color);
            else ColorMixer.Apply(AppServices.Engine, fixture, color, ColorBrightness(fixture));
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
            foreach (var effect in AppServices.Engine.Programmer.Effects.Where(ef => !ef.Type.IsMovement() && ef.FixtureIds.Any(selected.Contains)))
            {
                effect.Speed = effect.Type == EffectType.Strobe ? Math.Max(SpeedSlider.Value * 4, 2) : SpeedSlider.Value;
                effect.Size = SizeSlider.Value;
                effect.Spread = SpreadSlider.Value;
            }
        }
    }

    // ------------------------------------------------------------------ movement

    private static List<PatchedFixture> SelectedMovers() =>
        SelectedFixtures().Where(f => f.Has(ChannelType.Pan) || f.Has(ChannelType.Tilt)).ToList();

    /// <summary>One entry in the shape list: a built-in movement or a shape from the show's library.</summary>
    private sealed record ShapeChoice(string Label, EffectType Type, Guid? ShapeId)
    {
        public override string ToString() => Label;
    }

    private sealed record AreaChoice(string Label, Guid? AreaId)
    {
        public override string ToString() => Label;
    }

    private bool _fillingMoveLists;

    /// <summary>Refills the shape and area lists from the show, keeping the current choice.</summary>
    private void FillMovementLists()
    {
        var show = AppServices.Engine.Show;
        var shape = ShapeBox.SelectedItem as ShapeChoice;
        var area = AreaBox.SelectedItem as AreaChoice;
        _fillingMoveLists = true;
        try
        {
            ShapeBox.Items.Clear();
            foreach (var type in new[] { EffectType.Circle, EffectType.Figure8, EffectType.PanSweep, EffectType.TiltSweep })
                ShapeBox.Items.Add(new ShapeChoice(type.DisplayName(), type, null));
            foreach (var s in show.Shapes) ShapeBox.Items.Add(new ShapeChoice($"★ {s.Name}", EffectType.Shape, s.Id));
            ShapeBox.SelectedItem = ShapeBox.Items.OfType<ShapeChoice>().FirstOrDefault(c => c.Type == shape?.Type && c.ShapeId == shape?.ShapeId)
                                    ?? ShapeBox.Items[0];

            AreaBox.Items.Clear();
            AreaBox.Items.Add(new AreaChoice("No area (free)", null));
            foreach (var a in show.Areas) AreaBox.Items.Add(new AreaChoice(a.Name, a.Id));
            AreaBox.SelectedItem = AreaBox.Items.OfType<AreaChoice>().FirstOrDefault(c => c.AreaId == area?.AreaId) ?? AreaBox.Items[0];
        }
        finally
        {
            _fillingMoveLists = false;
        }
    }

    /// <summary>Copies the Movement card's settings into an effect.</summary>
    private void ApplyMoveSettings(Effect effect)
    {
        if (ShapeBox.SelectedItem is ShapeChoice shape)
        {
            effect.Type = shape.Type;
            effect.ShapeId = shape.ShapeId;
        }
        effect.AreaId = (AreaBox.SelectedItem as AreaChoice)?.AreaId;
        effect.Speed = MoveSpeedSlider.Value;
        effect.Size = MoveSizeSlider.Value;
        effect.Width = MoveWidthSlider.Value;
        effect.Height = MoveHeightSlider.Value;
        effect.Spread = MoveSpreadSlider.Value;
        effect.Reverse = MoveReverseBox.IsChecked == true;
    }

    private void Movement_Click(object sender, RoutedEventArgs e)
    {
        var movers = SelectedMovers();
        if (movers.Count == 0) return;
        var effect = new Effect { FixtureIds = movers.Select(f => f.Instance.Id).ToList() };
        ApplyMoveSettings(effect);
        AppServices.Engine.AddEffect(effect);
    }

    private void StopMovement_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected.ToHashSet();
        lock (AppServices.Engine.SyncRoot)
        {
            foreach (var effect in AppServices.Engine.Programmer.Effects.Where(ef => ef.Type.IsMovement() && ef.FixtureIds.Any(selected.Contains)).ToList())
                AppServices.Engine.Programmer.RemoveEffect(effect.Id);
        }
    }

    /// <summary>Changes to the Movement card apply straight away to movements running on the selected lights.</summary>
    private void UpdateRunningMovement()
    {
        if (_fillingMoveLists || MoveReverseBox is null || MoveSpreadSlider is null || ShapeBox is null || AreaBox is null) return;
        var selected = Selected.ToHashSet();
        lock (AppServices.Engine.SyncRoot)
        {
            foreach (var effect in AppServices.Engine.Programmer.Effects.Where(ef => ef.Type.IsMovement() && ef.FixtureIds.Any(selected.Contains)))
                ApplyMoveSettings(effect);
        }
    }

    private void MoveParam_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => UpdateRunningMovement();

    private void MoveSetting_Changed(object sender, RoutedEventArgs e) => UpdateRunningMovement();

    private void MoveChoice_Changed(object sender, SelectionChangedEventArgs e) => UpdateRunningMovement();

    private async void Shapes_Click(object sender, RoutedEventArgs e)
    {
        await Dialogs.ShapeLibraryDialog.ShowLibraryAsync();
        FillMovementLists();
    }

    private async void Areas_Click(object sender, RoutedEventArgs e)
    {
        await Dialogs.AreaDialog.ShowAreasAsync();
        FillMovementLists();
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
        ChannelType.Red, ChannelType.Green, ChannelType.Blue,
    };

    private readonly Dictionary<ChannelType, (Slider Slider, PatchedFixture Owner)> _channelSliders = new();

    /// <summary>Moves the "More controls" sliders to the current values, e.g. after the dimmer changed them.</summary>
    private void RefreshChannelSliders()
    {
        _syncing = true;
        try
        {
            foreach (var (type, (slider, owner)) in _channelSliders)
                if (Read(owner, type) is double v && slider.Value != v) slider.Value = v;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void BuildChannelRows(List<PatchedFixture> fixtures, bool colorCardShown)
    {
        ChannelRows.Children.Clear();
        _channelSliders.Clear();
        var types = fixtures.SelectMany(f => f.Channels.Select(c => c.Channel.Type))
            .Where(t => !HandledElsewhere.Contains(t) && !(t == ChannelType.White && colorCardShown))
            .Distinct()
            .OrderBy(t => t.Group())
            .ThenBy(t => t)
            .ToList();

        ChannelsCard.Visibility = types.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        string? group = null;
        foreach (var type in types)
        {
            // A small heading whenever the group changes (Color, Beam, Shutter, …).
            if (type.Group() != group)
            {
                group = type.Group();
                ChannelRows.Children.Add(new TextBlock
                {
                    Text = group == "Color" ? "Color & white" : group,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    Margin = new Thickness(0, ChannelRows.Children.Count == 0 ? 0 : 8, 0, 2),
                });
            }
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
            _channelSliders[type] = (slider, owner);

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
        UpdatePresetValues(rebuild: true);
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

    private readonly Dictionary<ChannelKey, (Slider Slider, NumberBox Box, TextBlock Meaning, ChannelDefinition Channel)> _presetRows = new();
    private string _presetSignature = "";

    /// <summary>
    /// Keeps the "In this preset" card in step with the programmer: rebuilt when channels or effects are
    /// added or removed, otherwise only the numbers are refreshed (except the one being typed in).
    /// </summary>
    private void UpdatePresetValues(bool rebuild = false)
    {
        bool editing = AppServices.PresetEditor.IsEditing;
        PresetValuesCard.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (!editing)
        {
            if (_presetRows.Count > 0 || _presetSignature.Length > 0) { PresetValueRows.Children.Clear(); _presetRows.Clear(); _presetSignature = ""; }
            return;
        }

        var snapshot = AppServices.Engine.ProgrammerSnapshot();
        var signature = string.Join("|", snapshot.Values.Keys.Select(k => $"{k.FixtureId}/{k.Channel}").Order()) + "#" +
                        string.Join("|", snapshot.Effects.Select(e => $"{e.Id}:{e.FixtureIds.Count}"));
        if (rebuild || signature != _presetSignature)
        {
            _presetSignature = signature;
            BuildPresetValues(snapshot);
            return;
        }

        _syncing = true;
        try
        {
            foreach (var (key, row) in _presetRows)
            {
                if (!snapshot.Values.TryGetValue(key, out var value)) continue;
                if (row.Slider.Value != value) row.Slider.Value = value;
                if (row.Box.FocusState == FocusState.Unfocused && row.Box.Value != value) row.Box.Value = value;
                row.Meaning.Text = Meaning(row.Channel, value);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static string Meaning(ChannelDefinition channel, byte value)
    {
        var cap = channel.Capabilities.FirstOrDefault(c => value >= c.From && value <= c.To);
        return cap is not null ? cap.Label : $"{Math.Round(value / 2.55)}%";
    }

    private void BuildPresetValues(ProgrammerSnapshot snapshot)
    {
        PresetValueRows.Children.Clear();
        _presetRows.Clear();
        var fixtures = AppServices.Engine.Fixtures;
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        foreach (var fixture in AppServices.Engine.Show.Fixtures)
        {
            if (!fixtures.TryGetValue(fixture.Id, out var patched)) continue;
            var channels = patched.Channels.Where(c => snapshot.Values.ContainsKey(new ChannelKey(fixture.Id, c.Channel.Name))).ToList();
            var effects = snapshot.Effects.Where(e => e.FixtureIds.Contains(fixture.Id)).ToList();
            if (channels.Count == 0 && effects.Count == 0) continue;

            var header = new HyperlinkButton
            {
                Content = $"{fixture.Name}  ·  {patched.Definition.DisplayName}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Padding = new Thickness(0, 6, 0, 2),
            };
            var id = fixture.Id;
            header.Click += (_, _) => AppServices.Selection.Set(new[] { id });
            ToolTipService.SetToolTip(header, "Select only this light");
            PresetValueRows.Children.Add(header);

            foreach (var ch in channels)
            {
                var key = new ChannelKey(fixture.Id, ch.Channel.Name);
                byte value = snapshot.Values[key];
                var row = new Grid { ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = ch.Channel.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                var meaning = new TextBlock { Text = Meaning(ch.Channel, value), Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], Foreground = secondary, TextTrimming = TextTrimming.CharacterEllipsis };
                label.Children.Add(meaning);
                ToolTipService.SetToolTip(label, ch.Channel.Name);

                var slider = new Slider { Minimum = 0, Maximum = 255, Value = value, VerticalAlignment = VerticalAlignment.Center };
                var box = new NumberBox
                {
                    Minimum = 0, Maximum = 255, Value = value, VerticalAlignment = VerticalAlignment.Center,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                    ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, $"{fixture.Name} {ch.Channel.Name}");
                var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 11 }, Padding = new Thickness(6, 4, 6, 4), VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetToolTip(remove, "Take this setting out of the preset");
                var channelName = ch.Channel.Name;
                remove.Click += (_, _) => AppServices.Engine.ReleaseChannel(id, channelName);

                void Set(double v)
                {
                    if (_syncing || double.IsNaN(v)) return;
                    var b = (byte)Math.Clamp(Math.Round(v), 0, 255);
                    AppServices.Engine.SetValue(id, channelName, b);
                    _syncing = true;
                    slider.Value = b;
                    box.Value = b;
                    meaning.Text = Meaning(ch.Channel, b);
                    _syncing = false;
                }
                slider.ValueChanged += (_, e) => Set(e.NewValue);
                box.ValueChanged += (_, e) => Set(e.NewValue);

                Grid.SetColumn(slider, 1);
                Grid.SetColumn(box, 2);
                Grid.SetColumn(remove, 3);
                row.Children.Add(label);
                row.Children.Add(slider);
                row.Children.Add(box);
                row.Children.Add(remove);
                PresetValueRows.Children.Add(row);
                _presetRows[key] = (slider, box, meaning, ch.Channel);
            }

            foreach (var effect in effects)
            {
                var row = new Grid { ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = $"Effect: {Core.Presets.PresetNamer.EffectName(AppServices.Engine.Show, effect)} (speed {effect.Speed:0.##}, on {effect.FixtureIds.Count} light{(effect.FixtureIds.Count == 1 ? "" : "s")})",
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 11 }, Padding = new Thickness(6, 4, 6, 4) };
                ToolTipService.SetToolTip(remove, "Take this effect out of the preset (for all its lights)");
                var effectId = effect.Id;
                remove.Click += (_, _) => AppServices.Engine.RemoveEffect(effectId);
                var edit = new Button { Content = "Edit…", Padding = new Thickness(8, 4, 8, 4) };
                ToolTipService.SetToolTip(edit, "Change speed, size, spread and, for movement, shape, area and direction");
                edit.Click += async (_, _) =>
                {
                    await Dialogs.EffectEditDialog.EditAsync(effectId);
                    UpdatePresetValues(rebuild: true);
                };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                buttons.Children.Add(edit);
                buttons.Children.Add(remove);
                Grid.SetColumn(buttons, 1);
                row.Children.Add(buttons);
                PresetValueRows.Children.Add(row);
            }
        }

        if (PresetValueRows.Children.Count == 0)
            PresetValueRows.Children.Add(new TextBlock { Text = "This preset is empty. Set some lights with the controls to add them.", TextWrapping = TextWrapping.Wrap, Foreground = secondary });
    }

    private void PresetEditSave_Click(object sender, RoutedEventArgs e) => AppServices.PresetEditor.Save();

    private void PresetEditCancel_Click(object sender, RoutedEventArgs e) => AppServices.PresetEditor.Cancel();

    private async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.MainWindow is not null) await AppServices.MainWindow.Presets.RecordPresetAsync();
    }
}
