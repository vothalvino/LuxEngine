using Lumeshow.App.Dialogs;
using Lumeshow.App.Helpers;
using Lumeshow.App.Services;
using Lumeshow.Core.Shows;
using Lumeshow.Output.Drivers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Lumeshow.App;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _statusTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _autosaveTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _beatTimer;
    private bool _closeConfirmed;
    private bool _updatingMaster;

    public Views.CueView Cues => CueView;
    public Views.PresetPanel Presets => PresetPanel;

    public MainWindow()
    {
        InitializeComponent();
        SetUpKeyboard();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lumeshow.ico"));
        FitToScreen();
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
        // A MIDI preset button works like clicking the preset: on the UI thread, selecting its lights.
        AppServices.Midi.PresetRequested += id => DispatcherQueue.TryEnqueue(() => Views.PresetPanel.UsePreset(id, layer: false));
        AppServices.Midi.Error += ex => DispatcherQueue.TryEnqueue(() => App.LogError(ex));
        AppServices.Remote.ActionPerformed += () => DispatcherQueue.TryEnqueue(SyncLiveControls);
        AppServices.RemoteServer.Error += ex => DispatcherQueue.TryEnqueue(() => App.LogError(ex));

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(150);
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        // The dot on the Tap button lights up on every beat and fades until the next.
        _beatTimer = DispatcherQueue.CreateTimer();
        _beatTimer.Interval = TimeSpan.FromMilliseconds(40);
        _beatTimer.Tick += (_, _) => BeatDot.Opacity = 1 - 0.85 * AppServices.Engine.BeatPhase;
        _beatTimer.Start();

        _autosaveTimer = DispatcherQueue.CreateTimer();
        _autosaveTimer.Interval = TimeSpan.FromSeconds(60);
        _autosaveTimer.Tick += (_, _) => AppServices.Document.Autosave();
        _autosaveTimer.Start();

        UpdateTitle();
        BuildRecentMenu();
        AutoUpdateCheckItem.IsChecked = AppServices.Settings.CheckForUpdates;
        // Test builds are version 0.0.0 and would always find an "update".
        bool released = (typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0)) > new Version(0, 0, 0, 0);
        if (released && AppServices.Settings.CheckForUpdates) _ = CheckForUpdatesAsync(byUser: false);
    }

    /// <summary>Opens at 1500×940, or maximized on screens too small for that.</summary>
    private void FitToScreen()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        if (work.Width < 1560 || work.Height < 1000)
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
            return;
        }
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + (work.Width - 1500) / 2, work.Y + (work.Height - 940) / 2, 1500, 940));
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
                "Lumeshow was not closed normally last time. Do you want to restore the unsaved show?", "Restore"))
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
        UndoItem.IsEnabled = doc.CanUndo;
        RedoItem.IsEnabled = doc.CanRedo;
        ShowTitle.Text = "—  " + doc.DisplayName + (doc.IsDirty ? " •" : "");
        Title = $"{doc.DisplayName}{(doc.IsDirty ? " *" : "")} - Lumeshow";
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
            OutputLed.Fill = Ui.Brush("LumeDangerBrush");
            OutputStatusText.Text = errors.Count == 1 ? "Output problem" : $"{errors.Count} output problems";
        }
        else
        {
            OutputLed.Fill = Ui.Brush("LumeOkBrush");
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
        if (engine.Blackout) BlackoutButton.Background = Ui.Brush("LumeDangerBrush");
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

    /// <summary>One tap of the tempo (the Tap button or the T key).</summary>
    public void TapTempo()
    {
        AppServices.Engine.Tap();
        ShowBpm();
    }

    private void ShowBpm() => BpmText.Text = $"{Math.Round(AppServices.Engine.Bpm)} BPM";

    private void Tap_Click(object sender, RoutedEventArgs e) => TapTempo();

    private void BpmFlyout_Opening(object? sender, object e) => BpmBox.Value = Math.Round(AppServices.Engine.Bpm);

    private void BpmBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue) || Math.Abs(args.NewValue - Math.Round(AppServices.Engine.Bpm)) < 0.5) return;
        AppServices.Engine.Bpm = args.NewValue;
        ShowBpm();
    }

    private void BpmScale_Click(object sender, RoutedEventArgs e)
    {
        double factor = double.Parse((string)((FrameworkElement)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        AppServices.Engine.Bpm *= factor;
        BpmBox.Value = Math.Round(AppServices.Engine.Bpm);
        ShowBpm();
    }

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

    public void ShowControlTab() => ModeSelector.SelectedItem = ControlTab;

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
        var path = await DialogHelper.PickOpenFileAsync(ShowSerializer.FileExtension, ShowSerializer.LegacyFileExtension);
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
            path = await DialogHelper.PickSaveFileAsync(AppServices.Document.DisplayName, "Lumeshow show", ShowSerializer.FileExtension);
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

    /// <summary>
    /// File > Exit asks about unsaved changes like the close button does. (Window.Close() does not raise
    /// AppWindow.Closing, so without this the changes were lost and the recovery copy deleted.)
    /// </summary>
    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Document.IsDirty && !await ConfirmDiscardAsync()) return;
        _closeConfirmed = true;
        Close();
    }

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

    // ------------------------------------------------------------------ edit menu

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoOrRedo(redo: false);

    private void Redo_Click(object sender, RoutedEventArgs e) => UndoOrRedo(redo: true);

    /// <summary>Undo covers changes to the show (lights, groups, presets, cues, buttons, shapes, outputs), not live fader moves.</summary>
    private void UndoOrRedo(bool redo)
    {
        if (AppServices.PresetEditor.IsEditing)
        {
            ParamBar.ShowMessage("Save or cancel the preset you are editing first");
            return;
        }
        var doc = AppServices.Document;
        try
        {
            bool done = redo ? doc.Redo() : doc.Undo();
            ParamBar.ShowMessage(done
                ? (redo ? "Redone" : "Undone — Ctrl+Y puts it back")
                : (redo ? "Nothing to redo" : "Nothing to undo"));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            App.LogError(ex);
            ParamBar.ShowMessage("That step could not be restored");
        }
    }

    private async void NewGroup_Click(object sender, RoutedEventArgs e) => await FixturePanel.NewGroupAsync();

    // ------------------------------------------------------------------ updates

    private Core.Updates.UpdateInfo? _update;

    /// <summary>Asks GitHub for the latest release. Nothing about you or your show is sent.</summary>
    private async Task CheckForUpdatesAsync(bool byUser)
    {
        var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
        try
        {
            if (!byUser) await Task.Delay(4000);   // not during start-up
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Lumeshow/" + current.ToString(3));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(Core.Updates.UpdateCheck.LatestReleaseApi);
            _update = Core.Updates.UpdateCheck.FromJson(json, current);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline or GitHub unreachable: try again next time.
            if (byUser) await DialogHelper.ShowMessageAsync("Could not check for updates", "GitHub could not be reached. Check your internet connection and try again.");
            return;
        }

        UpdateButton.Visibility = _update is null ? Visibility.Collapsed : Visibility.Visible;
        if (_update is not null) UpdateText.Text = $"Update {_update.Version} available";
        if (!byUser) return;
        if (_update is null)
            await DialogHelper.ShowMessageAsync("Lumeshow is up to date", $"You have the newest version ({current.ToString(3)}).");
        else if (await DialogHelper.ConfirmAsync($"Lumeshow {_update.Version} is available",
                     $"You have {current.ToString(3)}. Open the download page? Install the new version over this one; your shows and settings are kept.", "Open download page"))
            OpenUpdatePage();
    }

    private void OpenUpdatePage() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_update?.Url ?? Core.Updates.UpdateCheck.ReleasesPage) { UseShellExecute = true });

    private void Update_Click(object sender, RoutedEventArgs e) => OpenUpdatePage();

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(byUser: true);

    private void AutoUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.CheckForUpdates = AutoUpdateCheckItem.IsChecked;
        AppServices.Settings.Save();
    }

    // ------------------------------------------------------------------ other menus

    private async void AddFixture_Click(object sender, RoutedEventArgs e) => await AddFixtureDialog.ShowAndAddAsync();

    private async void NewFixtureType_Click(object sender, RoutedEventArgs e) => await FixtureEditorDialog.CreateAsync();

    private async void Shapes_Click(object sender, RoutedEventArgs e) => await ShapeLibraryDialog.ShowLibraryAsync();

    private async void Areas_Click(object sender, RoutedEventArgs e) => await AreaDialog.ShowAreasAsync();

    private async void EditSelectedFixture_Click(object sender, RoutedEventArgs e)
    {
        var id = await PickFixtureAsync("Edit fixture", "Which light do you want to rename or re-address?", byType: false);
        if (id is Guid fixtureId) await FixturePropertiesDialog.EditAsync(fixtureId);
    }

    private async void EditFixtureType_Click(object sender, RoutedEventArgs e)
    {
        var id = await PickFixtureAsync("Edit fixture type", "Which type of light do you want to change? All lights of that type change with it.", byType: true);
        if (id is Guid fixtureId) await FixtureEditorDialog.EditAsync(fixtureId);
    }

    /// <summary>
    /// Uses the selected light when exactly one is selected (or all selected lights share a type);
    /// otherwise asks which light, or which type and mode, to edit.
    /// </summary>
    private static async Task<Guid?> PickFixtureAsync(string title, string question, bool byType)
    {
        var show = AppServices.Engine.Show;
        if (show.Fixtures.Count == 0)
        {
            await DialogHelper.ShowMessageAsync(title, "There are no lights in this show yet. Add some with \"Add fixture from library…\" first.");
            return null;
        }

        var selected = AppServices.Selection.Ids.Select(show.FindFixture).OfType<Core.Fixtures.FixtureInstance>().ToList();
        if (selected.Count == 1) return selected[0].Id;
        if (byType && selected.Count > 1 && selected.Select(f => (f.DefinitionId, f.ModeName)).Distinct().Count() == 1) return selected[0].Id;

        var choices = byType
            ? show.Fixtures.GroupBy(f => (f.DefinitionId, f.ModeName)).Select(g =>
              {
                  var def = show.DefinitionOf(g.First());
                  return (Id: g.First().Id, Text: $"{def?.DisplayName ?? g.Key.DefinitionId} · {g.Key.ModeName}  ({g.Count()} light{(g.Count() == 1 ? "" : "s")})");
              }).ToList()
            : show.Fixtures.Select(f => (Id: f.Id, Text: $"{f.Name}  ·  universe {f.Universe}, address {f.Address}")).ToList();

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
        foreach (var c in choices) list.Items.Add(c.Text);
        list.SelectedIndex = 0;
        var panel = new StackPanel { Spacing = 10, Width = 420 };
        panel.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(list);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Edit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        list.DoubleTapped += (_, _) => { dialog.Hide(); list.Tag = "go"; };
        var result = await DialogHelper.ShowAsync(dialog);
        if ((result != ContentDialogResult.Primary && list.Tag as string != "go") || list.SelectedIndex < 0) return null;
        return choices[list.SelectedIndex].Id;
    }

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
            await DialogHelper.ShowMessageAsync("Could not import", "The file is not a Lumeshow fixture file.\n\n" + ex.Message);
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

    private async void Remote_Click(object sender, RoutedEventArgs e) => await DialogHelper.ShowAsync(new RemoteDialog());

    private async void Demo_Click(object sender, RoutedEventArgs e)
    {
        var demo = Path.Combine(AppContext.BaseDirectory, "samples", "Demo" + ShowSerializer.FileExtension);
        if (!File.Exists(demo)) return;
        // Open as an untitled copy so the bundled demo is never overwritten.
        if (await ConfirmDiscardAsync()) TryOpen(demo, keepPath: false);
    }

    private async void QuickStart_Click(object sender, RoutedEventArgs e) => await QuickStartDialog.ShowAsync();

    private async void Shortcuts_Click(object sender, RoutedEventArgs e) => await ShortcutsDialog.ShowAsync(this);

    private void GitHub_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/vothalvino/Lumeshow") { UseShellExecute = true });

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        await DialogHelper.ShowMessageAsync("About Lumeshow",
            $"Lumeshow {version}\nFree, open-source DMX lighting control for Windows.\n\n" +
            "MIT License. Fixture definitions from the Open Fixture Library (MIT).\nhttps://github.com/vothalvino/Lumeshow");
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
        // Parts of the window built in code pick their colours again for the new theme.
        DispatcherQueue.TryEnqueue(Ui.RaiseThemeChanged);
        // A tick in front of the theme in use.
        FontIcon? Tick(bool on) => on ? new FontIcon { Glyph = "\uE73E" } : null;
        ThemeDefault.Icon = Tick(theme is not ("Dark" or "Light"));
        ThemeDark.Icon = Tick(theme == "Dark");
        ThemeLight.Icon = Tick(theme == "Light");

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
