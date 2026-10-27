using System.IO.Ports;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Shows;
using LuxEngine.Output.Drivers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuxEngine.App.Dialogs;

/// <summary>Choose where DMX goes: Art-Net, sACN or a USB interface, per universe.</summary>
public sealed class OutputsDialog : ContentDialog
{
    private static readonly (OutputType Type, string Label)[] Types =
    {
        (OutputType.ArtNet, "Art-Net (network)"),
        (OutputType.Sacn, "sACN / E1.31 (network)"),
        (OutputType.EnttecPro, "Enttec DMX USB Pro"),
        (OutputType.OpenDmx, "Open DMX USB"),
    };

    private sealed class Row
    {
        public required OutputConfig Config;
        public required FrameworkElement View;
    }

    private readonly List<Row> _rows = new();
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly Slider _rate = new() { Minimum = 10, Maximum = 44, StepFrequency = 1, Width = 200, Header = "Frames per second" };
    private readonly IReadOnlyList<(string Label, string Ip)> _adapters = LocalAdapters();

    private OutputsDialog()
    {
        Title = "DMX outputs";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        foreach (var config in AppServices.Engine.Show.Outputs)
            AddRow(new OutputConfig
            {
                Id = config.Id, Type = config.Type, Enabled = config.Enabled, Universe = config.Universe,
                Target = config.Target, NetworkUniverse = config.NetworkUniverse, LocalAddress = config.LocalAddress,
            });

        var add = new Button { Content = "+ Add output" };
        add.Click += (_, _) =>
        {
            int universe = _rows.Count == 0 ? 1 : _rows.Max(r => r.Config.Universe) + 1;
            AddRow(new OutputConfig { Type = OutputType.ArtNet, Universe = universe, Target = "255.255.255.255", NetworkUniverse = universe - 1 });
        };

        _rate.Value = AppServices.Output.RefreshRate;

        var help = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Text = "Art-Net and sACN send DMX over your network to a DMX node or to lights with a network port. " +
                   "USB interfaces show up as a COM port once their FTDI driver is installed. " +
                   "Each output sends one LuxEngine universe (512 channels).",
        };

        var layout = new StackPanel { Spacing = 12, Width = 900 };
        layout.Children.Add(help);
        if (SerialPort.GetPortNames().Length == 0)
        {
            var link = new HyperlinkButton { Content = "Download the FTDI USB driver", NavigateUri = new Uri("https://ftdichip.com/drivers/vcp-drivers/") };
            layout.Children.Add(new InfoBar
            {
                IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Informational,
                Title = "No USB DMX interface found",
                Message = "If you use an Enttec or similar USB interface, plug it in. If it still doesn't appear, install the FTDI driver.",
                ActionButton = link,
            });
        }
        layout.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 420 });
        layout.Children.Add(add);
        layout.Children.Add(_rate);
        Content = layout;
    }

    private void AddRow(OutputConfig config)
    {
        var enabled = new CheckBox { IsChecked = config.Enabled, MinWidth = 0, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        ToolTipService.SetToolTip(enabled, "Send this output");

        var type = new ComboBox { Header = "Type", Width = 190 };
        foreach (var (_, label) in Types) type.Items.Add(label);
        type.SelectedIndex = Array.FindIndex(Types, t => t.Type == config.Type);

        var universe = new NumberBox { Header = "Universe", Value = config.Universe, Minimum = 1, Maximum = 64000, Width = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var target = new ComboBox { Header = "Send to", IsEditable = true, Width = 190 };
        var netUniverse = new NumberBox { Header = "Net universe", Value = config.NetworkUniverse, Minimum = 0, Maximum = 63999, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var adapter = new ComboBox { Header = "Network adapter", Width = 190 };
        adapter.Items.Add("Automatic");
        foreach (var (label, _) in _adapters) adapter.Items.Add(label);
        int adapterIndex = _adapters.ToList().FindIndex(a => a.Ip == config.LocalAddress);
        adapter.SelectedIndex = adapterIndex >= 0 ? adapterIndex + 1 : 0;

        var status = new FontIcon { Glyph = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) };
        var live = AppServices.Output.Outputs.FirstOrDefault(o => o.Universe == config.Universe);
        status.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[live?.State == OutputState.Error ? "LuxDangerBrush" : "LuxOkBrush"];
        ToolTipService.SetToolTip(status, live is null ? "Not running" : live.State == OutputState.Error ? live.LastError : "Running");

        var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 }, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 7, 8, 7) };

        void UpdateForType()
        {
            var t = Types[Math.Max(0, type.SelectedIndex)].Type;
            bool network = t is OutputType.ArtNet or OutputType.Sacn;
            netUniverse.Visibility = adapter.Visibility = network ? Visibility.Visible : Visibility.Collapsed;
            string current = target.Text;
            target.Items.Clear();
            if (t == OutputType.ArtNet)
            {
                target.Items.Add("255.255.255.255");
                target.Items.Add("2.255.255.255");
                target.Items.Add("10.255.255.255");
                target.PlaceholderText = "Broadcast or node IP";
                netUniverse.Header = "Art-Net universe";
                netUniverse.Minimum = 0;
            }
            else if (t == OutputType.Sacn)
            {
                target.PlaceholderText = "Multicast (leave empty)";
                netUniverse.Header = "sACN universe";
                netUniverse.Minimum = 1;
                if (netUniverse.Value < 1) netUniverse.Value = 1;
            }
            else
            {
                foreach (var port in SerialPort.GetPortNames().OrderBy(p => p)) target.Items.Add(port);
                target.PlaceholderText = "COM port";
            }
            target.Text = current;
        }

        target.Text = config.Target;
        UpdateForType();
        type.SelectionChanged += (_, _) =>
        {
            var t = Types[Math.Max(0, type.SelectedIndex)].Type;
            target.Text = t switch
            {
                OutputType.ArtNet => "255.255.255.255",
                OutputType.Sacn => "",
                _ => SerialPort.GetPortNames().FirstOrDefault() ?? "",
            };
            UpdateForType();
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        panel.Children.Add(enabled);
        panel.Children.Add(type);
        panel.Children.Add(universe);
        panel.Children.Add(target);
        panel.Children.Add(netUniverse);
        panel.Children.Add(adapter);
        panel.Children.Add(status);
        panel.Children.Add(remove);
        var card = new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = panel, Padding = new Thickness(10) };

        var row = new Row { Config = config, View = card };
        remove.Click += (_, _) =>
        {
            _rows.Remove(row);
            _list.Children.Remove(card);
        };

        // Read the controls back when saving.
        PrimaryButtonClick += (_, _) =>
        {
            config.Enabled = enabled.IsChecked == true;
            config.Type = Types[Math.Max(0, type.SelectedIndex)].Type;
            config.Universe = double.IsNaN(universe.Value) ? 1 : (int)universe.Value;
            config.Target = (target.Text ?? "").Trim();
            config.NetworkUniverse = double.IsNaN(netUniverse.Value) ? 0 : (int)netUniverse.Value;
            config.LocalAddress = adapter.SelectedIndex > 0 ? _adapters[adapter.SelectedIndex - 1].Ip : "";
        };

        _rows.Add(row);
        _list.Children.Add(card);
    }

    private static IReadOnlyList<(string Label, string Ip)> LocalAdapters()
    {
        var list = new List<(string, string)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            foreach (var addr in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                var ip = addr.Address.ToString();
                if (ip.StartsWith("127.")) continue;
                list.Add(($"{ip} ({nic.Name})", ip));
            }
        }
        catch (NetworkInformationException)
        {
        }
        return list;
    }

    public static async Task ShowAndApplyAsync()
    {
        var dialog = new OutputsDialog();
        if (await DialogHelper.ShowAsync(dialog) != ContentDialogResult.Primary) return;

        var outputs = dialog._rows.Select(r => r.Config).ToList();
        foreach (var o in outputs.Where(o => o.Type is OutputType.ArtNet or OutputType.Sacn && o.Target.Length > 0))
        {
            if (!System.Net.IPAddress.TryParse(o.Target, out _))
            {
                await DialogHelper.ShowMessageAsync("Check the address", $"\"{o.Target}\" is not a valid IP address.");
                return;
            }
        }

        AppServices.Output.RefreshRate = dialog._rate.Value;
        AppServices.Settings.RefreshRate = dialog._rate.Value;
        AppServices.Settings.Save();
        AppServices.Document.Edit(ShowChange.Outputs, show => show.Outputs = outputs);
    }
}
