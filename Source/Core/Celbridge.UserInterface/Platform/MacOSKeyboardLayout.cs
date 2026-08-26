using System.Runtime.InteropServices;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Resolves the ASCII letter a hardware key carries on the ASCII-capable keyboard layout, so a Command
/// shortcut still matches while a non-Latin layout is active and the key reports its own alphabet's letter
/// instead (Cyrillic, Greek, Hebrew). AppKit matches menu key equivalents through the same layout, so this
/// keeps the shortcuts the native key monitor delivers in step with the menu items beside them. macOS-only.
/// </summary>
internal static class MacOSKeyboardLayout
{
    private const string HIToolbox = "/System/Library/Frameworks/Carbon.framework/Versions/A/Frameworks/HIToolbox.framework/Versions/A/HIToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // kUCKeyActionDisplay asks what the key carries on its keycap, which needs no key-down state of its own.
    private const ushort KeyActionDisplay = 3;

    // kUCKeyTranslateNoDeadKeysMask stops a dead key (an accent) from swallowing the translation.
    private const uint TranslateNoDeadKeysMask = 1;

    private static readonly IntPtr RtldDefault = new(-2);

    [DllImport(HIToolbox)]
    private static extern IntPtr TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

    [DllImport(HIToolbox)]
    private static extern IntPtr TISGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);

    [DllImport(HIToolbox)]
    private static extern byte LMGetKbdType();

    [DllImport(HIToolbox)]
    private static extern int UCKeyTranslate(
        IntPtr keyLayoutPointer,
        ushort virtualKeyCode,
        ushort keyAction,
        uint modifierKeyState,
        uint keyboardType,
        uint keyTranslateOptions,
        ref uint deadKeyState,
        nuint maxStringLength,
        out nuint actualStringLength,
        [Out] char[] unicodeString);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);

    [DllImport(LibSystem)]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    /// <summary>
    /// The lower-case ASCII letter the given hardware key code carries on the ASCII-capable keyboard layout,
    /// or null when it carries none or the layout cannot be read.
    /// </summary>
    public static char? ResolveAsciiLetter(ulong keyCode)
    {
        var inputSource = TISCopyCurrentASCIICapableKeyboardLayoutInputSource();
        if (inputSource == IntPtr.Zero)
        {
            return null;
        }

        // Resolved on each call rather than cached: the layout answers the input source the user has
        // selected, and this runs at keypress rate on the few chords that reach it.
        try
        {
            var layoutData = GetUnicodeKeyLayoutData(inputSource);
            if (layoutData == IntPtr.Zero)
            {
                return null;
            }

            return TranslateToAsciiLetter(layoutData, (ushort)keyCode);
        }
        finally
        {
            CFRelease(inputSource);
        }
    }

    // The layout bytes UCKeyTranslate reads. The input source owns them, so they stay valid only while it is
    // alive.
    private static IntPtr GetUnicodeKeyLayoutData(IntPtr inputSource)
    {
        // The property key is an exported CFStringRef, so its symbol holds a pointer to the string. Copying
        // the input source has already loaded HIToolbox, so the default search finds it.
        var propertyKeySymbol = dlsym(RtldDefault, "kTISPropertyUnicodeKeyLayoutData");
        if (propertyKeySymbol == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var propertyKey = Marshal.ReadIntPtr(propertyKeySymbol);

        var layoutDataReference = TISGetInputSourceProperty(inputSource, propertyKey);
        if (layoutDataReference == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return CFDataGetBytePtr(layoutDataReference);
    }

    private static char? TranslateToAsciiLetter(IntPtr layoutData, ushort keyCode)
    {
        uint deadKeyState = 0;
        var characters = new char[4];

        var status = UCKeyTranslate(
            layoutData,
            keyCode,
            KeyActionDisplay,
            modifierKeyState: 0,
            LMGetKbdType(),
            TranslateNoDeadKeysMask,
            ref deadKeyState,
            (nuint)characters.Length,
            out var characterCount,
            characters);

        if (status != 0
            || characterCount == 0)
        {
            return null;
        }

        var character = char.ToLowerInvariant(characters[0]);
        if (character is < 'a' or > 'z')
        {
            return null;
        }

        return character;
    }
}
