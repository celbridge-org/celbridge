using Celbridge.Platform;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Resolves the platform-specific keyboard modifiers and keys for the standard edit shortcuts, so no
/// surface checks them itself. The command modifier is Control on Windows and Linux, and Command (which
/// the macOS Skia head surfaces as the left Windows key) on macOS; the delete key is Delete on every
/// head, plus Backspace on macOS.
/// </summary>
public static class EditKeyboard
{
    /// <summary>
    /// Whether the platform command modifier (Control, or Command on macOS) is currently down.
    /// </summary>
    public static bool IsCommandModifierDown()
    {
        var control = IsKeyDown(VirtualKey.Control);

        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (platformInfo.CommandModifier == CommandModifierKey.Command)
        {
            // The macOS head surfaces only the left Command key as a key code; Control stays folded in
            // as the fallback (matching MainPage's undo/redo handling), so this is additive.
            var command = IsKeyDown(VirtualKey.LeftWindows);
            return control || command;
        }

        return control;
    }

    /// <summary>
    /// Whether the Shift modifier is currently down.
    /// </summary>
    public static bool IsShiftDown() => IsKeyDown(VirtualKey.Shift);

    /// <summary>
    /// Whether the Alt modifier (Option on macOS) is currently down.
    /// </summary>
    public static bool IsAltDown() => IsKeyDown(VirtualKey.Menu);

    /// <summary>
    /// Whether the key is the platform delete key (Delete, plus Backspace on macOS).
    /// </summary>
    public static bool IsDeleteKey(VirtualKey key)
    {
        if (key == VirtualKey.Delete)
        {
            return true;
        }

        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        return platformInfo.TreatsBackspaceAsDeleteKey && key == VirtualKey.Back;
    }

    private static bool IsKeyDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
