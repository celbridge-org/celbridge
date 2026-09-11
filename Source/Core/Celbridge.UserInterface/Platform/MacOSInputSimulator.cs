using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Posts a synthetic key press into the application's own event queue with -[NSApplication
/// postEvent:atStart:], so the event travels the same route a real key press takes from the queue onward:
/// the NSEvent local monitor, UNOWindow sendEvent:, the key-equivalent phase, then the responder chain and
/// any focused WKWebView. It exists for keys an external test harness cannot deliver, and covers everything
/// except the window server's hand-off to the process. macOS-only.
/// </summary>
internal static class MacOSInputSimulator
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // NSEventType values.
    private const ulong EventTypeKeyDown = 10;
    private const ulong EventTypeKeyUp = 11;

    /// <summary>
    /// The keys that can be pressed, mapped to their hardware key code and the characters AppKit reports
    /// for them. An allowlist of non-printable keys by design: printable characters already reach the app
    /// through ordinary text input, so nothing is gained by synthesising them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, KeyMapping> Keys =
        new Dictionary<string, KeyMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = new(53, "\u001B"),
            ["Tab"] = new(48, "\u0009"),
            ["Return"] = new(36, "\u000D"),
            ["Backspace"] = new(51, "\u007F"),
            ["ForwardDelete"] = new(117, "\uF728"),
            ["Home"] = new(115, "\uF729"),
            ["End"] = new(119, "\uF72B"),
            ["PageUp"] = new(116, "\uF72C"),
            ["PageDown"] = new(121, "\uF72D"),
            ["Up"] = new(126, "\uF700"),
            ["Down"] = new(125, "\uF701"),
            ["Left"] = new(123, "\uF702"),
            ["Right"] = new(124, "\uF703"),
            ["F1"] = new(122, "\uF704"),
            ["F2"] = new(120, "\uF705"),
            ["F3"] = new(99, "\uF706"),
            ["F4"] = new(118, "\uF707"),
            ["F5"] = new(96, "\uF708"),
            ["F6"] = new(97, "\uF709"),
            ["F7"] = new(98, "\uF70A"),
            ["F8"] = new(100, "\uF70B"),
            ["F9"] = new(101, "\uF70C"),
            ["F10"] = new(109, "\uF70D"),
            ["F11"] = new(103, "\uF70E"),
            ["F12"] = new(111, "\uF70F"),
        };

    /// <summary>
    /// The key names this simulator accepts, in declaration order, for error messages and the guide.
    /// </summary>
    public static IReadOnlyList<string> SupportedKeys { get; } = Keys.Keys.ToList();

    /// <summary>
    /// Posts a key-down followed by a key-up for the named key to the key window. Fails when the platform
    /// is not macOS, the key name is not one this simulator knows, or the application has no key window to
    /// receive the press.
    /// </summary>
    public static Result PressKey(string key, bool command, bool control, bool shift, bool option)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Result.Fail("Simulated key presses are supported on macOS only.");
        }

        if (!Keys.TryGetValue(key, out var mapping))
        {
            return Result.Fail(
                $"Unknown key '{key}'. Supported keys: {string.Join(", ", SupportedKeys)}.");
        }

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        var keyWindow = SendMessage(application, GetSelector("keyWindow"));
        if (keyWindow == IntPtr.Zero)
        {
            return Result.Fail("The application has no key window to receive the key press.");
        }

        var windowNumber = SendMessageReturnNint(keyWindow, GetSelector("windowNumber"));

        ulong modifierFlags = 0;
        if (command)
        {
            modifierFlags |= MacOSKeyboardModifiers.CommandFlag;
        }
        if (control)
        {
            modifierFlags |= MacOSKeyboardModifiers.ControlFlag;
        }
        if (shift)
        {
            modifierFlags |= MacOSKeyboardModifiers.ShiftFlag;
        }
        if (option)
        {
            modifierFlags |= MacOSKeyboardModifiers.OptionFlag;
        }

        // stringWithUTF8String: returns an autoreleased NSString; this runs on the main thread, which has
        // a pool, and AppKit retains what it needs from the event.
        var characters = CreateNSString(mapping.Characters);

        PostKeyEvent(application, EventTypeKeyDown, modifierFlags, windowNumber, characters, mapping.KeyCode);
        PostKeyEvent(application, EventTypeKeyUp, modifierFlags, windowNumber, characters, mapping.KeyCode);

        return Result.Ok();
    }

    private static void PostKeyEvent(
        IntPtr application,
        ulong eventType,
        ulong modifierFlags,
        nint windowNumber,
        IntPtr characters,
        ushort keyCode)
    {
        var keyEvent = SendMessageKeyEvent(
            GetClass("NSEvent"),
            GetSelector("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:" +
                        "characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
            (nuint)eventType,
            default,
            (nuint)modifierFlags,
            0,
            windowNumber,
            IntPtr.Zero,
            characters,
            characters,
            false,
            keyCode);

        if (keyEvent == IntPtr.Zero)
        {
            return;
        }

        SendMessagePostEvent(application, GetSelector("postEvent:atStart:"), keyEvent, false);
    }

    private readonly record struct KeyMapping(ushort KeyCode, string Characters);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageKeyEvent(
        IntPtr receiver,
        IntPtr selector,
        nuint eventType,
        CGPoint location,
        nuint modifierFlags,
        double timestamp,
        nint windowNumber,
        IntPtr context,
        IntPtr characters,
        IntPtr charactersIgnoringModifiers,
        [MarshalAs(UnmanagedType.I1)] bool isARepeat,
        ushort keyCode);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessagePostEvent(
        IntPtr receiver,
        IntPtr selector,
        IntPtr keyEvent,
        [MarshalAs(UnmanagedType.I1)] bool atStart);
}
