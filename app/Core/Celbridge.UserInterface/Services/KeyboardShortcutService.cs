using Celbridge.Commands;
using Celbridge.Logging;
using Windows.System;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Centralized service for handling global keyboard shortcuts.
/// Handles shortcuts from both WinUI controls and WebView2-hosted content.
/// </summary>
public class KeyboardShortcutService : IKeyboardShortcutService
{
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<KeyboardShortcutService> _logger;

    public KeyboardShortcutService(
        ICommandService commandService,
        IMessengerService messengerService,
        ILogger<KeyboardShortcutService> logger)
    {
        _commandService = commandService;
        _messengerService = messengerService;
        _logger = logger;
    }

    public bool HandleShortcut(VirtualKey key, bool control, bool shift, bool alt)
    {
        // F11 shortcut toggles Zen Mode (fullscreen with panels hidden)
        if (key == VirtualKey.F11)
        {
            _logger.LogDebug("F11 pressed - toggling Zen Mode");
            _commandService.Execute<ISetLayoutCommand>(command =>
            {
                command.Transition = WindowModeTransition.ToggleZenMode;
            });
            return true;
        }

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
            "F11" => VirtualKey.F11,
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
