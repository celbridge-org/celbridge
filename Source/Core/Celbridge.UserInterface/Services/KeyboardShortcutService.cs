using Windows.System;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Handles the keyboard shortcuts no focused surface owns, from both WinUI controls and WebView2-hosted
/// content.
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
        // Every shortcut here is a chord, so an unmodified key cannot match one. Characters typed into a
        // hosted editor reach this method as managed key events.
        if (!control
            && !shift
            && !alt)
        {
            return false;
        }

        // All platforms close all documents shortcut: Ctrl+Shift+W
        if (control && shift && key == VirtualKey.W)
        {
            var message = new CloseAllDocumentsRequestedMessage();
            _messengerService.Send(message);
            return true;
        }

        // All platforms close active document shortcut: Ctrl+W
        if (control && key == VirtualKey.W)
        {
            var message = new CloseActiveDocumentRequestedMessage();
            _messengerService.Send(message);
            return true;
        }

        return false;
    }

    public bool HandleShortcut(string key, bool control, bool shift, bool alt)
    {
        var virtualKey = key switch
        {
            "w" or "W" => VirtualKey.W,
            _ => VirtualKey.None
        };

        if (virtualKey == VirtualKey.None)
        {
            return false;
        }

        return HandleShortcut(virtualKey, control, shift, alt);
    }
}
