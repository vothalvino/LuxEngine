using LuxEngine.App.Helpers;
using LuxEngine.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LuxEngine.App.Dialogs;

/// <summary>All keyboard shortcuts, grouped and searchable, including the keys given to presets and cue buttons.</summary>
public static class ShortcutsDialog
{
    private static bool _open;

    public static async Task ShowAsync(MainWindow window)
    {
        if (_open) return;
        var show = AppServices.Engine.Show;
        var rows = window.Shortcuts.Select(s => (s.Group, s.Keys, s.Description)).ToList();
        rows.AddRange(show.PresetRoot.AllPresets().Where(p => p.Key is not null)
            .Select(p => ("Your keys", KeyNames.Display(p.Key), $"Preset \"{p.Name}\" (Ctrl+key adds it on top)")));
        rows.AddRange(show.ButtonPage.Buttons.Where(b => b.Key is not null)
            .Select(b => ("Your keys", KeyNames.Display(b.Key), $"Cue button \"{b.Name}\"{(b.Mode == Core.Cues.CueButtonMode.Flash ? " (while held)" : "")}")));

        var search = new TextBox { PlaceholderText = "Search shortcuts, e.g. dimmer or gobo" };
        var list = new StackPanel { Spacing = 2 };
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        void Fill()
        {
            list.Children.Clear();
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var group in rows.Where(r => words.All(w => $"{r.Keys} {r.Description} {r.Group}".Contains(w, StringComparison.OrdinalIgnoreCase)))
                                      .GroupBy(r => r.Group))
            {
                list.Children.Add(new TextBlock
                {
                    Text = group.Key, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 12, 0, 4),
                });
                foreach (var (_, keys, description) in group)
                {
                    var row = new Grid { ColumnSpacing = 12 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    var key = new Border
                    {
                        Child = new TextBlock { Text = keys, FontFamily = new FontFamily("Consolas"), FontSize = 13 },
                        Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1), BorderBrush = secondary, HorizontalAlignment = HorizontalAlignment.Left,
                    };
                    var text = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(text, 1);
                    row.Children.Add(key);
                    row.Children.Add(text);
                    list.Children.Add(row);
                }
            }
            if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = "No shortcut matches.", Foreground = secondary });
        }
        search.TextChanged += (_, _) => Fill();
        Fill();

        var panel = new StackPanel { Spacing = 10, Width = 560 };
        panel.Children.Add(search);
        panel.Children.Add(new TextBlock
        {
            Text = "Give presets and cue buttons their own keys: right-click a preset > Set keyboard key, or edit a cue button.",
            TextWrapping = TextWrapping.Wrap, Foreground = secondary, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 440, Padding = new Thickness(0, 0, 12, 0) });

        _open = true;
        try
        {
            await DialogHelper.ShowAsync(new ContentDialog { Title = "Keyboard shortcuts", Content = panel, CloseButtonText = "Close" });
        }
        finally
        {
            _open = false;
        }
    }
}
