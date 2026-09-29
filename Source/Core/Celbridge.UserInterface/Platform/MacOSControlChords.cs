namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Recognises the Control chords that would type a control character into a text control. The decision is a
/// pure function of the key event's fields, so it is tested on every platform. macOS-only in use.
/// </summary>
internal static class MacOSControlChords
{
    // Hardware key codes of the keys whose control characters a text control acts on.
    private const ulong TabKeyCode = 48;
    private const ulong ReturnKeyCode = 36;
    private const ulong BackspaceKeyCode = 51;
    private const ulong EscapeKeyCode = 53;
    private const ulong KeypadEnterKeyCode = 76;

    /// <summary>
    /// Whether the chord holds Control but not Command, and types a C0 control character that no text control
    /// acts on. Tab, Return, Escape and Backspace type control characters too, but they are never counted.
    /// </summary>
    public static bool TypesControlCharacter(ulong modifierFlags, ulong keyCode, string characters)
    {
        bool control = (modifierFlags & MacOSKeyboardModifiers.ControlFlag) != 0;
        bool command = (modifierFlags & MacOSKeyboardModifiers.CommandFlag) != 0;

        if (!control
            || command)
        {
            return false;
        }

        if (keyCode is TabKeyCode
            or ReturnKeyCode
            or BackspaceKeyCode
            or EscapeKeyCode
            or KeypadEnterKeyCode)
        {
            return false;
        }

        return characters.Length == 1
            && characters[0] < ' ';
    }
}
