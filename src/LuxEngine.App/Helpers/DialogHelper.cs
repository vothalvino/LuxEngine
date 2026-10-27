using LuxEngine.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace LuxEngine.App.Helpers;

/// <summary>Small helpers for message boxes, text prompts and file pickers.</summary>
public static class DialogHelper
{
    private static XamlRoot Root => AppServices.MainWindow!.Content.XamlRoot;

    public static async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
        };
        await ShowAsync(dialog);
    }

    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Delete")
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>Asks the user to type a name. Returns null when cancelled.</summary>
    public static async Task<string?> PromptAsync(string title, string label, string initial = "", string okText = "OK")
    {
        var box = new TextBox { Header = label, Text = initial, SelectionStart = 0, SelectionLength = initial.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = box,
            PrimaryButtonText = okText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        var result = await ShowAsync(dialog);
        var text = box.Text.Trim();
        return result == ContentDialogResult.Primary && text.Length > 0 ? text : null;
    }

    private static bool _dialogOpen;

    /// <summary>Shows a dialog; WinUI allows only one at a time, so extra requests wait.</summary>
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        dialog.XamlRoot ??= Root;
        dialog.RequestedTheme = (AppServices.MainWindow?.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;
        while (_dialogOpen) await Task.Delay(50);
        _dialogOpen = true;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private static void Attach(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(AppServices.MainWindow!));

    public static async Task<string?> PickOpenFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
        Attach(picker);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickSaveFileAsync(string suggestedName, string typeName, string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(typeName, new List<string> { extension });
        Attach(picker);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
