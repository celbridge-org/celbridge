using Windows.System;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Centralized service for handling global keyboard shortcuts.
/// Handles shortcuts from both WinUI controls and WebView2-hosted content.
/// </summary>
public class KeyboardShortcutService : IKeyboardShortcutService
{
    private readonly IMessengerService _messengerService;

    public KeyboardShortcutService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public bool HandleShortcut(VirtualKey key, bool control, bool shift, bool alt)
    {
        // All platforms redo shortcut: Ctrl+Shift+Z
        if (control && shift && key == VirtualKey.Z)
        {
            var message = new RedoRequestedMessage();
            _messengerService.Send(message);
            return true;
        }

#if WINDOWS
        // Windows only redo shortcut: Ctrl+Y
        if (control && key == VirtualKey.Y)
        {
            var message = new RedoRequestedMessage();
            _messengerService.Send(message);
            return true;
        }
#endif

        // All platforms undo shortcut: Ctrl+Z
        if (control && key == VirtualKey.Z)
        {
            var message = new UndoRequestedMessage();
            _messengerService.Send(message);
            return true;
        }

        return false;
    }

    public bool HandleShortcut(string key, bool control, bool shift, bool alt)
    {
        var virtualKey = key switch
        {
            "z" or "Z" => VirtualKey.Z,
            "y" or "Y" => VirtualKey.Y,
            _ => VirtualKey.None
        };

        if (virtualKey == VirtualKey.None)
        {
            return false;
        }

        return HandleShortcut(virtualKey, control, shift, alt);
    }
}
