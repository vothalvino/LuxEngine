using LuxEngine.App.Dialogs;
using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using LuxEngine.Core.Shows;
using LuxEngine.Output.Drivers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace LuxEngine.App;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _statusTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _autosaveTimer;
    private bool _closeConfirmed;
    private bool _updatingMaster;

    public Views.CueView Cues => CueView;
    public Views.PresetPanel Presets => PresetPanel;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "LuxEngine.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 940));
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) =>
        {
            AppServices.Shutdown();
            ShowDocument.DeleteAutosave();
        };

        ApplyTheme(AppServices.Settings.Theme);
        AppServices.Document.StateChanged += UpdateTitle;
        AppServices.Document.Changed += OnShowChanged;
        AppServices.Midi.ActionPerformed += _ => DispatcherQueue.TryEnqueue(SyncLiveControls);

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(150);
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        _autosaveTimer = DispatcherQueue.CreateTimer();
        _autosaveTimer.Interval = TimeSpan.FromSeconds(60);
        _autosaveTimer.Tick += (_, _) => AppServices.Document.Autosave();
        _autosaveTimer.Start();

        UpdateTitle();
        BuildRecentMenu();
    }

    /// <summary>Called once after the window opened: recover, open a file, or welcome a new user.</summary>
    public async void StartUp(string? fileToOpen)
    {
        await Task.Delay(300); // let the window finish loading before showing dialogs

        if (AppServices.LibraryErrors.Count > 0)
            await DialogHelper.ShowMessageAsync("Some fixture files could not be read",
                string.Join("\n", AppServices.LibraryErrors.Take(10)));

        if (File.Exists(ShowDocument.AutosavePath) &&
            await DialogHelper.ConfirmAsync("Recover your show?",
                "LuxEngine was not closed normally last time. Do you want to restore the unsaved show?", "Restore"))
        {
            TryOpen(ShowDocument.AutosavePath, keepPath: false);
            return;
        }

        if (fileToOpen is not null)
        {
            TryOpen(fileToOpen);
        }
        else
        {
            AppServices.Document.New();
            if (!AppServices.Settings.FirstRunDone)
            {
                AppServices.Settings.FirstRunDone = true;
                AppServices.Settings.Save();
                await QuickStartDialog.ShowAsync();
            }
        }
    }

    // ------------------------------------------------------------------ status

    private void UpdateTitle()
    {
        var doc = AppServices.Document;
        ShowTitle.Text = "—  " + doc.DisplayName + (doc.IsDirty ? " •" : "");
        Title = $"{doc.DisplayName}{(doc.IsDirty ? " *" : "")} - LuxEngine";
    }

    private void OnShowChanged(ShowChange change)
    {
        if (change.HasFlag(ShowChange.Outputs)) AppServices.Output.Configure(AppServices.Engine.Show.Outputs);
        if (change.HasFlag(ShowChange.Patch))
            AppServices.Selection.Retain(AppServices.Engine.Show.Fixtures.Select(f => f.Id));
    }

    private void UpdateStatus()
    {
        var player = AppServices.Engine.CuePlayer;
        var cue = player.CurrentCue;
        CueStatus.Text = cue is null
            ? (player.IsFading ? "Releasing…" : "No cue running")
            : $"Cue {player.CurrentIndex + 1}: {cue.Name}{(player.IsPaused ? " (paused)" : player.IsFading ? $" ({player.Progress:P0})" : "")}";

        var outputs = AppServices.Output.Outputs;
        var errors = outputs.Where(o => o.State == OutputState.Error).ToList();
        if (outputs.Count == 0)
        {
            OutputLed.Fill = new SolidColorBrush(Colors.Gray);
            OutputStatusText.Text = "No output";
        }
        else if (errors.Count > 0)
        {
            OutputLed.Fill = (Brush)Application.Current.Resources["LuxDangerBrush"];
            OutputStatusText.Text = errors.Count == 1 ? "Output problem" : $"{errors.Count} output problems";
        }
        else
        {
            OutputLed.Fill = (Brush)Application.Current.Resources["LuxOkBrush"];
            OutputStatusText.Text = outputs.Count == 1 ? "Output live" : $"{outputs.Count} outputs live";
        }
        ToolTipService.SetToolTip(OutputStatusButton, outputs.Count == 0
            ? "No DMX output is set up. Click to add one."
            : string.Join("\n", outputs.Select(o => $"{o.Description}: {(o.State == OutputState.Error ? o.LastError : "OK")}")));

        SyncLiveControls();
    }

    private void SyncLiveControls()
    {
        var engine = AppServices.Engine;
        if (BlackoutButton.IsChecked != engine.Blackout) BlackoutButton.IsChecked = engine.Blackout;
        if (engine.Blackout) BlackoutButton.Background = (Brush)Application.Current.Resources["LuxDangerBrush"];
        else BlackoutButton.ClearValue(Microsoft.UI.Xaml.Controls.Control.BackgroundProperty);
        double master = Math.Round(engine.GrandMaster * 100);
        if (Math.Abs(MasterSlider.Value - master) > 0.5)
        {
            _updatingMaster = true;
            MasterSlider.Value = master;
            _updatingMaster = false;
        }
    }

    // ------------------------------------------------------------------ live controls

    private void MasterSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (MasterText is not null) MasterText.Text = $"{e.NewValue:0}%";
        if (!_updatingMaster) AppServices.Engine.GrandMaster = e.NewValue / 100;
    }

    private void Blackout_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Engine.Blackout = BlackoutButton.IsChecked == true;
        SyncLiveControls();
    }

    private void QuickGo_Click(object sender, RoutedEventArgs e) => CueView.Go();

    private void ModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool control = sender.SelectedItem == ControlTab;
        ControlView.Visibility = control ? Visibility.Visible : Visibility.Collapsed;
        CueView.Visibility = control ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowCueTab() => ModeSelector.SelectedItem = CueTab;

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        if (focused is TextBox or PasswordBox or AutoSuggestBox or RichEditBox or NumberBox) return;

        switch (e.Key)
        {
            case VirtualKey.Space:
                if (IsShiftDown()) AppServices.Engine.Back();
                else CueView.Go();
                e.Handled = true;
                break;
            case VirtualKey.B:
                AppServices.Engine.Blackout = !AppServices.Engine.Blackout;
                SyncLiveControls();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                AppServices.Selection.Clear();
                e.Handled = true;
                break;
        }
    }

    private static bool IsShiftDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ------------------------------------------------------------------ file menu

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!AppServices.Document.IsDirty) return true;
        var dialog = new ContentDialog
        {
            Title = "Save changes?",
            Content = $"Do you want to save the changes to \"{AppServices.Document.DisplayName}\"?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await DialogHelper.ShowAsync(dialog);
        if (result == ContentDialogResult.None) return false;
        if (result == ContentDialogResult.Primary) return await SaveAsync(false);
        return true;
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmDiscardAsync()) AppServices.Document.New();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = await DialogHelper.PickOpenFileAsync(ShowSerializer.FileExtension);
        if (path is not null) TryOpen(path);
    }

    private async void TryOpen(string path, bool keepPath = true)
    {
        try
        {
            AppServices.Document.Open(path, keepPath);
            if (keepPath)
            {
                AppServices.Settings.AddRecent(path);
                BuildRecentMenu();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            await DialogHelper.ShowMessageAsync("Could not open the show", ex.Message);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);

    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);

    private async Task<bool> SaveAsync(bool saveAs)
    {
        var path = AppServices.Document.FilePath;
        if (saveAs || path is null)
        {
            path = await DialogHelper.PickSaveFileAsync(AppServices.Document.DisplayName, "LuxEngine show", ShowSerializer.FileExtension);
            if (path is null) return false;
        }
        try
        {
            AppServices.Document.Save(path);
            AppServices.Settings.AddRecent(path);
            BuildRecentMenu();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await DialogHelper.ShowMessageAsync("Could not save the show", ex.Message);
            return false;
        }
    }

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        foreach (var path in AppServices.Settings.RecentShows.Where(File.Exists))
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileNameWithoutExtension(path), Tag = path };
            ToolTipService.SetToolTip(item, path);
            item.Click += async (s, _) =>
            {
                if (await ConfirmDiscardAsync()) TryOpen((string)((MenuFlyoutItem)s).Tag);
            };
            RecentMenu.Items.Add(item);
        }
        RecentMenu.IsEnabled = RecentMenu.Items.Count > 0;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !AppServices.Document.IsDirty) return;
        args.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ------------------------------------------------------------------ other menus

    private async void AddFixture_Click(object sender, RoutedEventArgs e) => await AddFixtureDialog.ShowAndAddAsync();

    private async void NewFixtureType_Click(object sender, RoutedEventArgs e) => await FixtureEditorDialog.CreateAsync();

    private async void ImportFixture_Click(object sender, RoutedEventArgs e)
    {
        var path = await DialogHelper.PickOpenFileAsync(".json");
        if (path is null) return;
        try
        {
            var definitions = Core.Fixtures.FixtureLibrary.ReadFile(path);
            foreach (var def in definitions)
            {
                def.IsUserDefined = true;
                Core.Fixtures.FixtureLibrary.SaveUserFixture(AppPaths.UserFixtures, def);
                AppServices.Library.Add(def);
            }
            await DialogHelper.ShowMessageAsync("Fixtures imported",
                $"Imported {definitions.Count} fixture type(s). You can now add them from the library.");
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or NullReferenceException)
        {
            await DialogHelper.ShowMessageAsync("Could not import", "The file is not a LuxEngine fixture file.\n\n" + ex.Message);
        }
    }

    private void OpenFixtureFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.UserFixtures);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.UserFixtures) { UseShellExecute = true });
    }

    private async void Outputs_Click(object sender, RoutedEventArgs e) => await OutputsDialog.ShowAndApplyAsync();

    private async void Monitor_Click(object sender, RoutedEventArgs e) => await DialogHelper.ShowAsync(new UniverseMonitorDialog());

    private async void Midi_Click(object sender, RoutedEventArgs e) => await DialogHelper.ShowAsync(new MidiDialog());

    private async void Demo_Click(object sender, RoutedEventArgs e)
    {
        var demo = Path.Combine(AppContext.BaseDirectory, "samples", "Demo.luxshow");
        if (!File.Exists(demo)) return;
        // Open as an untitled copy so the bundled demo is never overwritten.
        if (await ConfirmDiscardAsync()) TryOpen(demo, keepPath: false);
    }

    private async void QuickStart_Click(object sender, RoutedEventArgs e) => await QuickStartDialog.ShowAsync();

    private async void Shortcuts_Click(object sender, RoutedEventArgs e) =>
        await DialogHelper.ShowMessageAsync("Keyboard shortcuts",
            "Space — GO (next cue)\nShift+Space — Back\nB — Blackout on/off\nEsc — Deselect all fixtures\n" +
            "Ctrl+N / Ctrl+O / Ctrl+S — New / Open / Save show\nF11 — Full screen\n\n" +
            "In the stage view: click to select, Ctrl+click to add, drag on empty space to select an area.");

    private void GitHub_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/vothalvino/LuxEngine") { UseShellExecute = true });

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        await DialogHelper.ShowMessageAsync("About LuxEngine",
            $"LuxEngine {version}\nFree, open-source DMX lighting control for Windows.\n\n" +
            "MIT License. Fixture definitions from the Open Fixture Library (MIT).\nhttps://github.com/vothalvino/LuxEngine");
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        var kind = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Overlapped
            : AppWindowPresenterKind.FullScreen;
        AppWindow.SetPresenter(kind);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var theme = (string)((FrameworkElement)sender).Tag;
        AppServices.Settings.Theme = theme;
        AppServices.Settings.Save();
        ApplyTheme(theme);
    }

    private void ApplyTheme(string theme)
    {
        Root.RequestedTheme = theme switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        ThemeDefault.IsChecked = theme is not ("Dark" or "Light");
        ThemeDark.IsChecked = theme == "Dark";
        ThemeLight.IsChecked = theme == "Light";

        // Keep the minimize/maximize/close buttons readable on the custom title bar.
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(40, 255, 255, 255) : Windows.UI.Color.FromArgb(30, 0, 0, 0);
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
    }
}
