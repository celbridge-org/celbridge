using Windows.System;

namespace Celbridge.UserInterface;

/// <summary>
/// Service for handling global keyboard shortcuts across the application.
/// </summary>
public interface IKeyboardShortcutService
{
    /// <summary>
    /// Handles a global keyboard shortcut.
    /// </summary>
    bool HandleGlobalShortcut(VirtualKey key, bool control, bool shift, bool alt);

    /// <summary>
    /// Handles a keyboard shortcut message received from a WebView2 control.
    /// </summary>
    bool HandleWebView2KeyboardShortcut(string? jsonMessage);
}
