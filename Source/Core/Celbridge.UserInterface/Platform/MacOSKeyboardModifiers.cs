using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// The modifier keys held at the moment the state was read.
/// </summary>
internal record MacOSModifierState(bool Command, bool Control, bool Shift, bool Option);

/// <summary>
/// Reads the modifier keys AppKit reports as held right now. Uno derives its own modifier state from the
/// managed key events it raises, so a modifier whose release it never sees stays down for every later key.
/// AppKit answers for the hardware instead. macOS-only.
/// </summary>
internal static class MacOSKeyboardModifiers
{
    // NSEventModifierFlag bit positions.
    public const ulong ShiftFlag = 1UL << 17;
    public const ulong ControlFlag = 1UL << 18;
    public const ulong OptionFlag = 1UL << 19;
    public const ulong CommandFlag = 1UL << 20;

    /// <summary>
    /// The modifier keys currently held, or null on every other platform.
    /// </summary>
    public static MacOSModifierState? GetCurrentState()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var modifierFlags = (ulong)SendMessageReturnNuint(GetClass("NSEvent"), GetSelector("modifierFlags"));

        var command = (modifierFlags & CommandFlag) != 0;
        var control = (modifierFlags & ControlFlag) != 0;
        var shift = (modifierFlags & ShiftFlag) != 0;
        var option = (modifierFlags & OptionFlag) != 0;

        return new MacOSModifierState(command, control, shift, option);
    }
}
