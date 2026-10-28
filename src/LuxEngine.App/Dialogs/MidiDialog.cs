using LuxEngine.App.Services;
using LuxEngine.Control;
using LuxEngine.Core.Shows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuxEngine.App.Dialogs;

/// <summary>Connect faders and buttons of a MIDI controller to LuxEngine with "learn".</summary>
public sealed class MidiDialog : ContentDialog
{
    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        [MidiActions.GrandMaster] = "Master fader",
        [MidiActions.Go] = "GO (next cue)",
        [MidiActions.Back] = "Back",
        [MidiActions.Pause] = "Pause",
        [MidiActions.Release] = "Release",
        [MidiActions.Blackout] = "Blackout",
        [MidiActions.Preset] = "Use a preset",
        [MidiActions.CueButton] = "Cue button",
    };

    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly TextBlock _devices = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _lastMessage = new() { Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };

    public MidiDialog()
    {
        Title = "MIDI controllers";
        CloseButtonText = "Done";

        var add = new DropDownButton { Content = "+ Add mapping" };
        var menu = new MenuFlyout();
        foreach (var (action, label) in ActionLabels)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => AddMapping(action);
            menu.Items.Add(item);
        }
        add.Flyout = menu;

        var rescan = new Button { Content = "Reconnect devices" };
        rescan.Click += (_, _) =>
        {
            AppServices.Midi.Start();
            ShowDevices();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(add);
        buttons.Children.Add(rescan);

        var layout = new StackPanel { Spacing = 12, Width = 620 };
        layout.Children.Add(_devices);
        layout.Children.Add(new TextBlock
        {
            Text = "Add a mapping, click \"Learn\", then move a fader or press a button on your controller.",
            TextWrapping = TextWrapping.Wrap,
        });
        layout.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 360 });
        layout.Children.Add(buttons);
        layout.Children.Add(_lastMessage);
        Content = layout;

        ShowDevices();
        foreach (var mapping in AppServices.Engine.Show.MidiMappings) AddRow(mapping);

        AppServices.Midi.MessageReceived += OnMessage;
        Closed += (_, _) =>
        {
            AppServices.Midi.MessageReceived -= OnMessage;
            AppServices.Midi.CancelLearn();
        };
    }

    private void OnMessage(MidiInput input) =>
        DispatcherQueue.TryEnqueue(() => _lastMessage.Text = $"Last received: {input.Describe()} = {input.Value}");

    private void ShowDevices()
    {
        var names = AppServices.Midi.DeviceNames;
        _devices.Text = names.Count == 0
            ? "No MIDI controller found. Plug one in and click \"Reconnect devices\"."
            : "Connected: " + string.Join(", ", names);
    }

    private void AddMapping(string action)
    {
        var mapping = new MidiMapping { Action = action, Number = -1 };
        AppServices.Document.Edit(ShowChange.None, show => show.MidiMappings.Add(mapping));
        AddRow(mapping);
    }

    private void AddRow(MidiMapping mapping)
    {
        var label = new TextBlock { Text = ActionLabels.GetValueOrDefault(mapping.Action, mapping.Action), Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var input = new TextBlock { Width = 190, VerticalAlignment = VerticalAlignment.Center, Text = Describe(mapping) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(label);

        if (mapping.Action == MidiActions.Preset)
        {
            var presets = AppServices.Engine.Show.PresetRoot.AllPresets().ToList();
            var choose = new ComboBox { Width = 160, PlaceholderText = "Choose preset" };
            foreach (var p in presets) choose.Items.Add(p.Name);
            choose.SelectedIndex = presets.FindIndex(p => p.Id == mapping.Target);
            choose.SelectionChanged += (_, _) =>
            {
                if (choose.SelectedIndex >= 0)
                    AppServices.Document.Edit(ShowChange.None, _ => mapping.Target = presets[choose.SelectedIndex].Id);
            };
            row.Children.Add(choose);
        }
        else if (mapping.Action == MidiActions.CueButton)
        {
            var buttons = AppServices.Engine.Show.ButtonPage.Buttons.OrderBy(b => b.Row).ThenBy(b => b.Column).ToList();
            var choose = new ComboBox { Width = 160, PlaceholderText = "Choose button" };
            foreach (var b in buttons) choose.Items.Add(string.IsNullOrWhiteSpace(b.Name) ? $"Button {b.Row + 1}.{b.Column + 1}" : b.Name);
            choose.SelectedIndex = buttons.FindIndex(b => b.Id == mapping.Target);
            choose.SelectionChanged += (_, _) =>
            {
                if (choose.SelectedIndex >= 0)
                    AppServices.Document.Edit(ShowChange.None, _ => mapping.Target = buttons[choose.SelectedIndex].Id);
            };
            row.Children.Add(choose);
        }

        var learn = new Button { Content = "Learn" };
        learn.Click += (_, _) =>
        {
            input.Text = "Move a control now…";
            AppServices.Midi.Learn(msg => DispatcherQueue.TryEnqueue(() =>
            {
                AppServices.Document.Edit(ShowChange.None, _ =>
                {
                    mapping.MessageType = msg.MessageType;
                    mapping.Channel = msg.Channel;
                    mapping.Number = msg.Number;
                });
                input.Text = Describe(mapping);
            }));
        };
        var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 } };
        remove.Click += (_, _) =>
        {
            AppServices.Document.Edit(ShowChange.None, show => show.MidiMappings.Remove(mapping));
            _list.Children.Remove(row);
        };

        row.Children.Add(input);
        row.Children.Add(learn);
        row.Children.Add(remove);
        _list.Children.Add(row);
    }

    private static string Describe(MidiMapping m) => m.Number < 0
        ? "Not learned yet"
        : new MidiInput(m.MessageType, m.Channel, m.Number, 0).Describe();
}
