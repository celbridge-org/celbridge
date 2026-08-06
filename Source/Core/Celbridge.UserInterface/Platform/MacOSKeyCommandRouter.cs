using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.WebHost;
using Windows.System;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Routes the editing keys Uno's 6.6 key pipeline diverts away from the native first responder. The macOS
/// Skia canvas became an NSTextInputClient for IME support, and its processing splits keystrokes three
/// ways: printable keys reach a focused web view natively, the arrows become AppKit commands that travel up
/// the responder chain to the window, and Backspace and Enter are converted into managed KeyDown events
/// dispatched through the XAML tree. The two diverted kinds arrive at this router's two entry points:
/// commands at the window's doCommandBySelector: (added by Install), and the managed keys via
/// TryForwardManagedEditingKey from the root key handler. In both cases, when a hosted web
/// surface holds focus the underlying key event is delivered to its native web view, because the key never
/// reached the page; otherwise the command is absorbed, since the managed pipeline has already acted on the
/// key and AppKit's default would end the responder chain in noResponder: and beep. Introduced by Uno.Sdk
/// 6.5.36 to 6.6.29: the 6.5 native library contains none of that text-input machinery. Remove this once
/// Uno delivers editing keys to the native first responder itself. macOS-only.
/// </summary>
internal static class MacOSKeyCommandRouter
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // Uno's NSWindow subclass. Live windows are KVO-swizzled subclasses of it that inherit the added method,
    // so the base class is the stable place to add it.
    private const string WindowClassName = "UNOWindow";

    // Objective-C type encoding for -(void)doCommandBySelector:(SEL): void return, then self, _cmd, and the
    // selector argument.
    private const string DoCommandTypeEncoding = "v@::";

    // NSEventTypeKeyDown == 10.
    private const long EventTypeKeyDown = 10;

    [DllImport(LibObjC, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr classHandle, IntPtr selector, IntPtr implementation, string types);

    // kVK_Delete and kVK_Return, the hardware key codes of the keys Uno dispatches through the managed tree.
    private const ulong BackspaceKeyCode = 51;
    private const ulong ReturnKeyCode = 36;

    private static IWebViewFocusRegistry? _webViewFocusRegistry;

    // The timestamp of the last key event forwarded to a web view. A key the page leaves unhandled comes
    // back through this chain, so the same event is absorbed on its second arrival rather than re-forwarded.
    private static double _lastForwardedEventTimestamp;

    /// <summary>
    /// Adds the command-handling doCommandBySelector: to Uno's window class. Returns false when the class is
    /// not registered, and when Uno has started implementing the method itself, in which case its
    /// implementation is left in place.
    /// </summary>
    public static bool Install()
    {
        var windowClass = GetClass(WindowClassName);
        if (windowClass == IntPtr.Zero)
        {
            return false;
        }

        unsafe
        {
            var implementation = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&HandleCommand;
            return class_addMethod(windowClass, GetSelector("doCommandBySelector:"), implementation, DoCommandTypeEncoding);
        }
    }

    /// <summary>
    /// Supplies the registry that knows which web surface holds focus, enabling the forward path. Until it
    /// is set every command is absorbed.
    /// </summary>
    public static void SetFocusRegistry(IWebViewFocusRegistry webViewFocusRegistry)
    {
        _webViewFocusRegistry = webViewFocusRegistry;
    }

    /// <summary>
    /// Forwards the key event behind a managed KeyDown to the focused web surface. Uno dispatches Backspace
    /// and Enter through the managed tree rather than the command path the other editing keys take, so the
    /// root key handler calls this for every managed KeyDown. Returns false for any other key, when no web
    /// surface holds focus, and when the event being processed is not that key down, so managed handling
    /// proceeds.
    /// </summary>
    public static bool TryForwardManagedEditingKey(VirtualKey key)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var keyCode = ResolveManagedEditingKeyCode(key);
        if (keyCode is null)
        {
            return false;
        }

        return ForwardCurrentKeyEvent(keyCode.Value);
    }

    private static ulong? ResolveManagedEditingKeyCode(VirtualKey key)
    {
        return key switch
        {
            VirtualKey.Back => BackspaceKeyCode,
            VirtualKey.Enter => ReturnKeyCode,
            _ => null
        };
    }

    // Recovers the key event the application is processing and delivers it to the focused web surface.
    // Returns false, leaving the caller's handling to proceed, when there is no focused surface or the
    // current event is not the expected key down. The same event coming back through the responder chain
    // (a key the page left unhandled) is absorbed rather than re-forwarded.
    private static bool ForwardCurrentKeyEvent(ulong? expectedKeyCode)
    {
        var registry = _webViewFocusRegistry;
        if (registry is null)
        {
            return false;
        }

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        var currentEvent = SendMessage(application, GetSelector("currentEvent"));
        if (currentEvent == IntPtr.Zero)
        {
            return false;
        }

        var eventType = SendMessageReturnNint(currentEvent, GetSelector("type"));
        if (eventType != EventTypeKeyDown)
        {
            return false;
        }

        if (expectedKeyCode is not null)
        {
            var keyCode = SendMessageReturnNuint(currentEvent, GetSelector("keyCode")) & 0xFFFF;
            if (keyCode != expectedKeyCode.Value)
            {
                return false;
            }
        }

        var eventTimestamp = SendMessageReturnDouble(currentEvent, GetSelector("timestamp"));
        if (eventTimestamp == _lastForwardedEventTimestamp)
        {
            return false;
        }

        _lastForwardedEventTimestamp = eventTimestamp;

        return registry.TryForwardKeyEvent(currentEvent);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HandleCommand(IntPtr self, IntPtr command, IntPtr selector)
    {
        // Runs inside AppKit's key handling. Never let an exception cross back into native code, and absorb
        // the command on any failure: the pre-forward behaviour (a silently dropped key) beats a crash.
        try
        {
            ForwardCurrentKeyEvent(expectedKeyCode: null);
        }
        catch
        {
        }
    }
}
