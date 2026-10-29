using LuxEngine.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace LuxEngine.App.Views;

/// <summary>Click it, then press the key to use. Backspace or Delete removes the key.</summary>
public sealed class KeyCaptureBox : TextBox
{
    private string? _key;

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        PlaceholderText = "Click here, then press a key";
        Header = "Keyboard key";
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Tab) return;   // keep moving through the dialog
            e.Handled = true;
            if (e.Key is VirtualKey.Back or VirtualKey.Delete)
            {
                Key = null;
                Error = null;
            }
            else if (KeyNames.IsAssignable(e.Key))
            {
                Key = e.Key.ToString();
                Error = null;
            }
            else Error = KeyNames.Reason(e.Key);
            Changed?.Invoke();
        };
    }

    /// <summary>The chosen key as a VirtualKey name (e.g. "Number1"), or null for none.</summary>
    public string? Key
    {
        get => _key;
        set
        {
            _key = value;
            Text = KeyNames.Display(value);
        }
    }

    /// <summary>Why the last key pressed cannot be used, or null.</summary>
    public string? Error { get; private set; }

    public event Action? Changed;
}
