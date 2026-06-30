using Celbridge.Platform;
using Windows.System;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Handles global keyboard shortcuts from both WinUI controls and WebView2-hosted content.
/// </summary>
public class KeyboardShortcutService : IKeyboardShortcutService
{
    private readonly IMessengerService _messengerService;
    private readonly IPlatformInfo _platformInfo;

    public KeyboardShortcutService(IMessengerService messengerService, IPlatformInfo platformInfo)
    {
        _messengerService = messengerService;
        _platformInfo = platformInfo;
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

        // Windows redo shortcut: Ctrl+Y
        if (control && key == VirtualKey.Y && _platformInfo.TreatsCtrlYAsRedo)
        {
            var message = new RedoRequestedMessage();
            _messengerService.Send(message);
            return true;
        }

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
