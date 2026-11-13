using Celbridge.Platform;
using Celbridge.UserInterface.Platform;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Resolves the platform-specific keyboard modifiers and keys for the standard edit shortcuts and for a drag
/// that copies, so no surface checks them itself. The command modifier is Control on Windows and Linux, and
/// Command on macOS. The delete key is Delete on every head, plus Backspace on macOS. A drag copies with Control
/// held on Windows and Linux, and with Option held on macOS.
///
/// macOS answers every modifier question from AppKit rather than from the key state Uno accumulates: a
/// modifier released while a native web view holds the keyboard never reaches Uno's managed key pipeline,
/// so its cached state stays down and every later keystroke reads as a chord.
/// </summary>
public static class EditKeyboard
{
    /// <summary>
    /// Whether the platform command modifier (Control, or Command on macOS) is currently down.
    /// </summary>
    public static bool IsCommandModifierDown()
    {
        var macOSModifiers = MacOSKeyboardModifiers.GetCurrentState();
        if (macOSModifiers is not null)
        {
            return macOSModifiers.Command;
        }

        return IsKeyDown(VirtualKey.Control);
    }

    /// <summary>
    /// Returns true if an input event's modifiers include the platform command modifier: Control, or Command on
    /// macOS. The event records the keys held at the time of the event. Those keys can differ from the keys held now.
    /// </summary>
    public static bool HasCommandModifier(VirtualKeyModifiers modifiers)
    {
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (platformInfo.CommandModifier == CommandModifierKey.Command)
        {
            // Uno reports Command as the Windows modifier.
            return modifiers.HasFlag(VirtualKeyModifiers.Windows);
        }

        return modifiers.HasFlag(VirtualKeyModifiers.Control);
    }

    /// <summary>
    /// Whether the Shift modifier is currently down.
    /// </summary>
    public static bool IsShiftDown()
    {
        var macOSModifiers = MacOSKeyboardModifiers.GetCurrentState();
        if (macOSModifiers is not null)
        {
            return macOSModifiers.Shift;
        }

        return IsKeyDown(VirtualKey.Shift);
    }

    /// <summary>
    /// Whether the Alt modifier (Option on macOS) is currently down.
    /// </summary>
    public static bool IsAltDown()
    {
        var macOSModifiers = MacOSKeyboardModifiers.GetCurrentState();
        if (macOSModifiers is not null)
        {
            return macOSModifiers.Option;
        }

        return IsKeyDown(VirtualKey.Menu);
    }

    /// <summary>
    /// Returns true if the modifier that makes a drag copy is down: Control, or Option on macOS.
    /// </summary>
    public static bool IsCopyDragModifierDown()
    {
        var macOSModifiers = MacOSKeyboardModifiers.GetCurrentState();
        if (macOSModifiers is not null)
        {
            return macOSModifiers.Option;
        }

        return IsKeyDown(VirtualKey.Control);
    }

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
