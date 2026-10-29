using LuxEngine.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LuxEngine.App.Dialogs;

/// <summary>A short, friendly tour shown on first start and from Help.</summary>
public static class QuickStartDialog
{
    private static readonly (string Glyph, string Title, string Text)[] Steps =
    {
        ("", "1. Connect your DMX output",
            "Output → DMX outputs. Pick Art-Net or sACN for network nodes, or your Enttec USB interface. The dot at the top shows when output is live."),
        ("", "2. Add your lights",
            "Click \"Add\" under Fixtures and search the library (hundreds of lights included). Can't find yours? Create it with the channel list from its manual."),
        ("", "3. Make a look",
            "Select lights in the list or on the stage, then use the Control tab: dimmer, color, position and effects."),
        ("", "4. Save it as a preset",
            "Click \"+ Preset\". Presets live in folders on the left — click one any time to bring that look back."),
        ("", "5. Build your cues",
            "Switch to the Cue tab, add a cue and drag several presets onto it — for example \"Blue wash\" + \"Spots center\". Set the fade time."),
        ("", "6. Run the show",
            "Press GO (or Space) to fade to the next cue. Use BLACKOUT and the Master fader for safety."),
    };

    public static async Task ShowAsync()
    {
        var panel = new StackPanel { Spacing = 14, Width = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "Welcome! Here is how to light your first show in six steps.",
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var (glyph, title, text) in Steps)
        {
            var row = new Grid { ColumnSpacing = 14 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new FontIcon { Glyph = glyph, FontSize = 22, Foreground = Ui.Brush("LuxAccentBrush"), VerticalAlignment = VerticalAlignment.Top };
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            texts.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
            Grid.SetColumn(texts, 1);
            row.Children.Add(icon);
            row.Children.Add(texts);
            panel.Children.Add(row);
        }

        var dialog = new ContentDialog
        {
            Title = "Getting started with LuxEngine",
            Content = new ScrollViewer { Content = panel },
            CloseButtonText = "Let's go",
            DefaultButton = ContentDialogButton.Close,
        };
        await DialogHelper.ShowAsync(dialog);
    }
}
