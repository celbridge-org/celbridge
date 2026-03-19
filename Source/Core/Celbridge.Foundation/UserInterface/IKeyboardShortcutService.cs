using Windows.System;

namespace Celbridge.UserInterface;

/// <summary>
/// Service for handling global keyboard shortcuts across the application.
/// </summary>
public interface IKeyboardShortcutService
{
    /// <summary>
    /// Handles a global keyboard shortcut using a VirtualKey.
    /// Used by WinUI keyboard handlers.
    /// </summary>
    bool HandleShortcut(VirtualKey key, bool control, bool shift, bool alt);

    /// <summary>
    /// Handles a global keyboard shortcut using a key name string.
    /// Used by JSON-RPC notifications from WebView2.
    /// </summary>
    bool HandleShortcut(string key, bool control, bool shift, bool alt);
}
