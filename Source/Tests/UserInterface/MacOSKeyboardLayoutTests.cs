using Celbridge.UserInterface.Platform;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the layout translation that lets a Command shortcut match while a non-Latin keyboard
/// layout is active. macOS-only, because it reads the live input source through Carbon. The assertions hold
/// whatever layout the machine is set to, since the translation asks the ASCII-capable layout.
/// </summary>
[TestFixture]
public class MacOSKeyboardLayoutTests
{
    // ANSI hardware key codes, which name a physical key rather than the letter any layout puts on it.
    private const ulong KeyCodeC = 8;
    private const ulong KeyCodeV = 9;
    private const ulong KeyCodeZ = 6;

    [Test]
    [Platform("MacOsX")]
    public void ResolveAsciiLetter_ForALetterKey_ReturnsThatLetter()
    {
        MacOSKeyboardLayout.ResolveAsciiLetter(KeyCodeC).Should().Be('c');
        MacOSKeyboardLayout.ResolveAsciiLetter(KeyCodeV).Should().Be('v');
        MacOSKeyboardLayout.ResolveAsciiLetter(KeyCodeZ).Should().Be('z');
    }

    [Test]
    [Platform("MacOsX")]
    public void ResolveAsciiLetter_ForAKeyCarryingNoLetter_ReturnsNull()
    {
        // kVK_Space and kVK_Escape carry no letter.
        MacOSKeyboardLayout.ResolveAsciiLetter(49).Should().BeNull();
        MacOSKeyboardLayout.ResolveAsciiLetter(53).Should().BeNull();
    }

    [Test]
    [Platform("MacOsX")]
    public void ResolveAsciiLetter_ForAKeyCodeNoKeyboardHas_ReturnsNullRatherThanThrowing()
    {
        // The key code comes straight from the event, so an unexpected one must not throw into AppKit.
        MacOSKeyboardLayout.ResolveAsciiLetter(0xFFFF).Should().BeNull();
    }
}
