using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// The modifier keys held for an AppKit event.
/// </summary>
internal record MacOSModifierState(bool Command, bool Control, bool Shift, bool Option);

/// <summary>
/// Reads the modifier keys from AppKit. Uno tracks the modifier keys from the managed key events. When Uno misses
/// the release of a modifier key, Uno reports that key down for every later key. AppKit reports the hardware.
/// macOS-only.
/// </summary>
internal static class MacOSKeyboardModifiers
{
    // NSEventModifierFlag bit positions.
    public const ulong ShiftFlag = 1UL << 17;
    public const ulong ControlFlag = 1UL << 18;
    public const ulong OptionFlag = 1UL << 19;
    public const ulong CommandFlag = 1UL << 20;

    /// <summary>
    /// Returns the modifier keys held when AppKit's current event was sent, or null on every other platform. With no
    /// current event, returns the modifier keys held now.
    /// </summary>
    public static MacOSModifierState? GetCurrentState()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        // An input handler gets the keys held for its own event. A synthetic chord can release its modifier before
        // the handler runs, so the keys held now can lack that modifier.
        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        var currentEvent = SendMessage(application, GetSelector("currentEvent"));

        ulong modifierFlags;
        if (currentEvent != IntPtr.Zero)
        {
            modifierFlags = (ulong)SendMessageReturnNuint(currentEvent, GetSelector("modifierFlags"));
        }
        else
        {
            modifierFlags = (ulong)SendMessageReturnNuint(GetClass("NSEvent"), GetSelector("modifierFlags"));
        }

        var command = (modifierFlags & CommandFlag) != 0;
        var control = (modifierFlags & ControlFlag) != 0;
        var shift = (modifierFlags & ShiftFlag) != 0;
        var option = (modifierFlags & OptionFlag) != 0;

        return new MacOSModifierState(command, control, shift, option);
    }
}
