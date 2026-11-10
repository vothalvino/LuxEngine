using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Control.Remote;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace LuxEngine.App.Dialogs;

/// <summary>Switches the phone remote on and shows the address and PIN to use on the phone.</summary>
public sealed class RemoteDialog : ContentDialog
{
    private readonly ToggleSwitch _enabled = new() { Header = "Phone remote", OnContent = "On", OffContent = "Off" };
    private readonly NumberBox _port = new()
    {
        Header = "Port", Minimum = 1024, Maximum = 65535, Width = 140,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
    };
    private readonly TextBox _pin = new() { Header = "PIN (empty = no PIN)", Width = 140, MaxLength = 12 };
    private readonly StackPanel _addresses = new() { Spacing = 4 };
    private readonly InfoBar _problem = new() { Severity = InfoBarSeverity.Error, IsClosable = false, IsOpen = false };
    private bool _loading = true;

    public RemoteDialog()
    {
        Title = "Phone remote";
        CloseButtonText = "Done";

        var settings = AppServices.Settings;
        _enabled.IsOn = AppServices.RemoteServer.IsRunning;
        _port.Value = settings.RemotePort;
        _pin.Text = settings.RemotePin;
        AutomationProperties.SetName(_enabled, "Phone remote");
        AutomationProperties.SetName(_port, "Remote port");
        AutomationProperties.SetName(_pin, "Remote PIN");

        var newPin = new Button { Content = "New PIN", VerticalAlignment = VerticalAlignment.Bottom };
        newPin.Click += (_, _) => _pin.Text = RemoteApi.NewPin();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(_port);
        row.Children.Add(_pin);
        row.Children.Add(newPin);

        var layout = new StackPanel { Spacing = 12, Width = 460 };
        layout.Children.Add(new TextBlock
        {
            Text = "Control GO, blackout, the master, tap tempo and your cue buttons from a phone or tablet. " +
                   "There is nothing to install: the phone opens a web page that LuxEngine serves itself.",
            TextWrapping = TextWrapping.Wrap,
        });
        layout.Children.Add(_enabled);
        layout.Children.Add(row);
        layout.Children.Add(_problem);
        layout.Children.Add(_addresses);
        layout.Children.Add(new TextBlock
        {
            Text = "The phone must be on the same network (Wi-Fi) as this PC. The first time, Windows may ask whether " +
                   "LuxEngine may be reached from the network: choose Allow. Anyone on the network who knows the PIN can " +
                   "use the remote, so keep the PIN on when the Wi-Fi is shared with guests.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = Ui.Brush("TextFillColorSecondaryBrush"),
        });
        Content = layout;

        _enabled.Toggled += (_, _) => Apply();
        _port.ValueChanged += (_, _) => Apply();
        _pin.TextChanged += (_, _) => Apply();
        _loading = false;
        ShowAddresses();
    }

    /// <summary>Saves the settings and restarts or stops the remote to match them.</summary>
    private void Apply()
    {
        if (_loading) return;
        var settings = AppServices.Settings;

        // The first time the remote is switched on it gets a PIN, so it is never open by accident.
        if (_enabled.IsOn && !settings.RemoteEnabled && settings.RemotePin.Length == 0 && _pin.Text.Length == 0)
        {
            _pin.Text = RemoteApi.NewPin();   // comes back here through TextChanged
            return;
        }

        settings.RemoteEnabled = _enabled.IsOn;
        if (!double.IsNaN(_port.Value)) settings.RemotePort = (int)Math.Clamp(_port.Value, 1024, 65535);
        settings.RemotePin = _pin.Text.Trim();
        settings.Save();

        string? problem = null;
        if (settings.RemoteEnabled) problem = AppServices.StartRemote();
        else AppServices.RemoteServer.Stop();
        _problem.Message = problem ?? "";
        _problem.IsOpen = problem is not null;
        ShowAddresses();
    }

    private void ShowAddresses()
    {
        _addresses.Children.Clear();
        if (!AppServices.RemoteServer.IsRunning) return;

        var urls = RemoteServer.Addresses(AppServices.RemoteServer.Port);
        _addresses.Children.Add(new TextBlock
        {
            Text = urls.Count == 0
                ? "This PC is not connected to a network. Connect it to the Wi-Fi the phone uses."
                : "On the phone, open the browser and type this address:",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var url in urls.Take(4))
        {
            var text = new TextBlock
            {
                Text = url,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var copy = new Button { Content = new SymbolIcon(Symbol.Copy) };
            ToolTipService.SetToolTip(copy, "Copy the address");
            AutomationProperties.SetName(copy, "Copy the address");
            copy.Click += (_, _) =>
            {
                var data = new DataPackage();
                data.SetText(url);
                Clipboard.SetContent(data);
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            line.Children.Add(text);
            line.Children.Add(copy);
            _addresses.Children.Add(line);
        }
        if (urls.Count > 1)
            _addresses.Children.Add(new TextBlock
            {
                Text = "This PC has more than one network. If the first address does not open, try the next.",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
        if (AppServices.Settings.RemotePin.Length == 0)
            _addresses.Children.Add(new TextBlock
            {
                Text = "No PIN: everyone on this network can use the remote.",
                Foreground = Ui.Brush("LuxDangerBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
    }
}
