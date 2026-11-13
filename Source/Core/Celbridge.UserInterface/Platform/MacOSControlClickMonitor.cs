using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Logging;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Turns a Control-click on the application's own controls into a secondary click, so the click opens a context
/// menu. AppKit views already treat a Control-click as a secondary click. An AppKit local monitor rewrites the
/// press, the drags that follow the press and the release, before Uno's window receives them. A Control-click in a
/// web view reaches WebKit unchanged, because WebKit opens its own menu for one. macOS-only.
/// </summary>
// UNO-BUG: Uno's macOS window raises a Control-click as a primary click with Control held, where an AppKit view
// treats it as a secondary click.
internal static class MacOSControlClickMonitor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // NSEventType values.
    private const nuint LeftMouseDown = 1;
    private const nuint LeftMouseUp = 2;
    private const nuint RightMouseDown = 3;
    private const nuint RightMouseUp = 4;
    private const nuint LeftMouseDragged = 6;
    private const nuint RightMouseDragged = 7;

    // NSEventMaskLeftMouseDown == 1 << 1, NSEventMaskLeftMouseUp == 1 << 2, NSEventMaskLeftMouseDragged == 1 << 6.
    private const ulong EventMaskLeftMouse = (1UL << 1) | (1UL << 2) | (1UL << 6);

    // An NSPoint is two doubles. The ARM64 ABI passes and returns them in the floating-point registers, so the
    // struct marshals by value through plain objc_msgSend.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern NSPoint SendMessageReturnNSPoint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageHitTest(IntPtr receiver, IntPtr selector, NSPoint point);

    // objc_msgSend for
    // +mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageCreateMouseEvent(
        IntPtr receiver,
        IntPtr selector,
        nuint type,
        NSPoint location,
        nuint modifierFlags,
        double timestamp,
        nint windowNumber,
        IntPtr context,
        nint eventNumber,
        nint clickCount,
        float pressure);

    private static bool _started;
    private static bool _isRewritingClick;
    private static IntPtr _unoWindowClass;
    private static IntPtr _webViewClass;
    private static ILogger? _logger;

    public static unsafe void Start(ILogger logger)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (_started)
        {
            return;
        }

        // Before macOS 15, Uno reads a click's button from the buttons held down, and ignores the event's type. A
        // rewritten click would arrive as a plain primary click, so the monitor stays off there.
        if (!OperatingSystem.IsMacOSVersionAtLeast(15))
        {
            return;
        }

        _unoWindowClass = GetClass("UNOWindow");
        _webViewClass = GetClass("WKWebView");
        if (_unoWindowClass == IntPtr.Zero
            || _webViewClass == IntPtr.Zero)
        {
            logger.LogWarning(
                "Cannot make Control-click a secondary click: the UNOWindow or WKWebView class is missing");
            return;
        }

        _started = true;
        _logger = logger;

        var handler = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&MonitorCallback;
        AddLocalEventMonitor(EventMaskLeftMouse, handler);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr MonitorCallback(IntPtr block, IntPtr nsEvent)
    {
        // Runs on the main thread during event dispatch. Never let an exception cross back into AppKit.
        try
        {
            var type = SendMessageReturnNuint(nsEvent, GetSelector("type"));

            // Each press decides whether the drags and the release that follow it are rewritten too.
            if (type == LeftMouseDown)
            {
                _isRewritingClick = IsControlClickOnApplication(nsEvent);
            }

            if (!_isRewritingClick)
            {
                return nsEvent;
            }

            if (type == LeftMouseUp)
            {
                _isRewritingClick = false;
            }

            return CreateSecondaryButtonEvent(nsEvent, type);
        }
        catch (Exception exception)
        {
            _isRewritingClick = false;
            _logger?.LogError(exception, "The Control-click monitor callback failed");
            return nsEvent;
        }
    }

    // Returns true for a press with Control in Uno's window, outside every web view.
    private static bool IsControlClickOnApplication(IntPtr nsEvent)
    {
        var modifierFlags = (ulong)SendMessageReturnNuint(nsEvent, GetSelector("modifierFlags"));
        if ((modifierFlags & MacOSKeyboardModifiers.ControlFlag) == 0)
        {
            return false;
        }

        var window = SendMessage(nsEvent, GetSelector("window"));
        if (window == IntPtr.Zero
            || !SendMessageReturnBool(window, GetSelector("isKindOfClass:"), _unoWindowClass))
        {
            return false;
        }

        var contentView = SendMessage(window, GetSelector("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return false;
        }

        // hitTest: takes a point in the superview's coordinates. The content view's superview uses window
        // coordinates, like locationInWindow.
        var location = SendMessageReturnNSPoint(nsEvent, GetSelector("locationInWindow"));
        var hitView = SendMessageHitTest(contentView, GetSelector("hitTest:"), location);
        if (hitView == IntPtr.Zero)
        {
            return false;
        }

        // A click in a web view hits one of the WKWebView's subviews, so check every view from the hit view up to
        // the window.
        var view = hitView;
        while (view != IntPtr.Zero)
        {
            if (SendMessageReturnBool(view, GetSelector("isKindOfClass:"), _webViewClass))
            {
                return false;
            }
            view = SendMessage(view, GetSelector("superview"));
        }

        return true;
    }

    // Builds the secondary button's copy of a primary button event, with Control taken out of its modifiers.
    private static IntPtr CreateSecondaryButtonEvent(IntPtr nsEvent, nuint type)
    {
        var secondaryType = RightMouseDragged;
        if (type == LeftMouseDown)
        {
            secondaryType = RightMouseDown;
        }
        else if (type == LeftMouseUp)
        {
            secondaryType = RightMouseUp;
        }

        var location = SendMessageReturnNSPoint(nsEvent, GetSelector("locationInWindow"));
        var modifierFlags = SendMessageReturnNuint(nsEvent, GetSelector("modifierFlags"))
            & ~(nuint)MacOSKeyboardModifiers.ControlFlag;
        var timestamp = SendMessageReturnDouble(nsEvent, GetSelector("timestamp"));
        var windowNumber = SendMessageReturnNint(nsEvent, GetSelector("windowNumber"));
        var eventNumber = SendMessageReturnNint(nsEvent, GetSelector("eventNumber"));
        var clickCount = SendMessageReturnNint(nsEvent, GetSelector("clickCount"));
        var pressure = secondaryType == RightMouseUp ? 0f : 1f;

        var selector = GetSelector("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:"
            + "eventNumber:clickCount:pressure:");

        return SendMessageCreateMouseEvent(
            GetClass("NSEvent"),
            selector,
            secondaryType,
            location,
            modifierFlags,
            timestamp,
            windowNumber,
            IntPtr.Zero,
            eventNumber,
            clickCount,
            pressure);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X;
        public double Y;
    }
}
