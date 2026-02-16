using System.Text.Json;
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

    public bool HandleGlobalShortcut(VirtualKey key, bool control, bool shift, bool alt)
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

    public bool HandleWebView2KeyboardShortcut(string? jsonMessage)
    {
        if (string.IsNullOrEmpty(jsonMessage))
        {
            return false;
        }

        // Quick check: keyboard shortcut messages are JSON objects starting with '{'
        if (!jsonMessage.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;

            // Check if this is a keyboard shortcut message
            if (!root.TryGetProperty("type", out var typeProp) ||
                typeProp.GetString() != "keyboard_shortcut")
            {
                return false;
            }

            // Extract key and modifier information
            var keyString = root.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
            var ctrlKey = root.TryGetProperty("ctrlKey", out var ctrlProp) && ctrlProp.GetBoolean();
            var shiftKey = root.TryGetProperty("shiftKey", out var shiftProp) && shiftProp.GetBoolean();
            var altKey = root.TryGetProperty("altKey", out var altProp) && altProp.GetBoolean();

            // Convert key string to VirtualKey
            var virtualKey = MapKeyStringToVirtualKey(keyString);
            if (virtualKey == VirtualKey.None)
            {
                return false;
            }

            return HandleGlobalShortcut(virtualKey, ctrlKey, shiftKey, altKey);
        }
        catch (JsonException)
        {
            // Not a valid JSON message, not handled as keyboard shortcut
            return false;
        }
    }

    private static VirtualKey MapKeyStringToVirtualKey(string? keyString)
    {
        return keyString switch
        {
            "F11" => VirtualKey.F11,
            "z" or "Z" => VirtualKey.Z,
            "y" or "Y" => VirtualKey.Y,
            _ => VirtualKey.None
        };
    }
}
