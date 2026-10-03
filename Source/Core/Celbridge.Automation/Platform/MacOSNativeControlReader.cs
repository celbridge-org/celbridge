using System.Runtime.InteropServices;
using Celbridge.WebHost.Platform;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.Automation.Platform;

/// <summary>
/// Reads what AppKit draws for the application: the menu bar's items, the main window's buttons and the native
/// web views. A frame is in points from the top left of the window's content view, which holds the managed
/// content. Call on the main (UI) thread, where AppKit is safe.
/// </summary>
internal class MacOSNativeControlReader : INativeControlReader
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // The NSWindowButton values of the window's close, minimize and zoom buttons.
    private const nuint CloseButton = 0;
    private const nuint MinimizeButton = 1;
    private const nuint ZoomButton = 2;

    // NSControlStateValueOn, the state of a menu item that shows its mark.
    private const nint StateOn = 1;

    private static readonly nuint[] WindowButtons =
    [
        CloseButton,
        MinimizeButton,
        ZoomButton
    ];

    // The bounds of a control with no frame in the window, such as a menu item.
    private static readonly ControlBounds NoFrame = new(0, 0, 0, 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    // A CGRect is four doubles, which the ARM64 ABI passes and returns in the floating point registers. So the
    // struct marshals by value through plain objc_msgSend.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGRect SendMessageReturnCGRect(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGRect SendMessageConvertRect(IntPtr receiver, IntPtr selector, CGRect rect, IntPtr view);

    public IReadOnlyList<ShowingControl> ReadControls()
    {
        var controls = new List<ShowingControl>();

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        var mainMenu = SendMessage(application, GetSelector("mainMenu"));
        if (mainMenu != IntPtr.Zero)
        {
            ReadMenuItems(mainMenu, controls);
        }

        var window = FindMainWindow(application);
        if (window != IntPtr.Zero)
        {
            ReadWindowButtons(window, controls);
        }

        return controls;
    }

    public NativeView? FindNativeView(FrameworkElement element)
    {
        if (element is not WebView2 webView)
        {
            return null;
        }

        // A web view whose native view cannot be found draws nothing.
        var notShowing = new NativeView(false, NoFrame);
        if (webView.CoreWebView2 is null ||
            !MacOSWebViewInterop.TryGetNativeWebViewHandle(webView.CoreWebView2, out var nativeWebView, out _))
        {
            return notShowing;
        }

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        var window = SendMessage(nativeWebView, GetSelector("window"));
        if (window == IntPtr.Zero ||
            window != FindMainWindow(application) ||
            !IsShowing(nativeWebView))
        {
            return notShowing;
        }

        var frame = FrameInContent(nativeWebView, window);

        return new NativeView(true, frame);
    }

    private static void ReadMenuItems(IntPtr menu, List<ShowingControl> controls)
    {
        // Validation sets each item's enabled state. Celbridge's own items also set their marks as they validate.
        SendMessage(menu, GetSelector("update"));

        var count = SendMessageReturnNint(menu, GetSelector("numberOfItems"));
        for (nint index = 0; index < count; index++)
        {
            var item = SendMessage(menu, GetSelector("itemAtIndex:"), index);
            if (item == IntPtr.Zero ||
                SendMessageReturnBool(item, GetSelector("isSeparatorItem")) ||
                SendMessageReturnBool(item, GetSelector("isHidden")))
            {
                continue;
            }

            var submenu = SendMessage(item, GetSelector("submenu"));
            controls.Add(DescribeMenuItem(menu, index, item, submenu));

            if (submenu != IntPtr.Zero)
            {
                ReadMenuItems(submenu, controls);
            }
        }
    }

    private static ShowingControl DescribeMenuItem(IntPtr menu, nint index, IntPtr item, IntPtr submenu)
    {
        // An item that opens a submenu shows no mark. Pressing it would open the menu, which blocks until it closes.
        bool? isChecked = null;
        var hasAction = false;
        if (submenu == IntPtr.Zero)
        {
            isChecked = SendMessageReturnNint(item, GetSelector("state")) == StateOn;
            hasAction = SendMessage(item, GetSelector("action")) != IntPtr.Zero;
        }

        var info = new ControlInfo(
            ReadNSString(SendMessage(item, GetSelector("identifier"))),
            ReadNSString(SendMessage(item, GetSelector("title"))),
            "MenuItem",
            GetClassName(item),
            NoFrame,
            SendMessageReturnBool(item, GetSelector("isEnabled")),
            isChecked,
            null);

        return new ShowingControl(info, () => PerformMenuItem(menu, index, hasAction));
    }

    private static ControlAction? PerformMenuItem(IntPtr menu, nint index, bool hasAction)
    {
        if (!hasAction)
        {
            return null;
        }

        SendMessageVoid(menu, GetSelector("performActionForItemAtIndex:"), index);

        return ControlAction.Invoke;
    }

    private static void ReadWindowButtons(IntPtr window, List<ShowingControl> controls)
    {
        foreach (var kind in WindowButtons)
        {
            // In full screen the title bar moves to a window of its own.
            var button = SendMessage(window, GetSelector("standardWindowButton:"), kind);
            if (button == IntPtr.Zero ||
                SendMessage(button, GetSelector("window")) != window ||
                !IsShowing(button))
            {
                continue;
            }

            // The button's cell is its accessibility element. It gives the button no title or identifier, and its
            // subrole names the button in every language.
            var cell = SendMessage(button, GetSelector("cell"));
            var info = new ControlInfo(
                ReadNSString(SendMessage(cell, GetSelector("accessibilitySubrole"))),
                ReadNSString(SendMessage(cell, GetSelector("accessibilityRoleDescription"))),
                "Button",
                GetClassName(button),
                FrameInContent(button, window),
                SendMessageReturnBool(button, GetSelector("isEnabled")),
                null,
                null);

            controls.Add(new ShowingControl(info, () => PressButton(button)));
        }
    }

    private static ControlAction? PressButton(IntPtr button)
    {
        SendMessageVoid(button, GetSelector("performClick:"), IntPtr.Zero);

        return ControlAction.Invoke;
    }

    // The application has one window of Uno's class. Live windows are subclasses of it, so the class is matched by
    // kind.
    private static IntPtr FindMainWindow(IntPtr application)
    {
        var windowClass = GetClass("UNOWindow");
        var windows = SendMessage(application, GetSelector("windows"));
        if (windowClass == IntPtr.Zero ||
            windows == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var count = SendMessageReturnNint(windows, GetSelector("count"));
        for (nint index = 0; index < count; index++)
        {
            var window = SendMessage(windows, GetSelector("objectAtIndex:"), index);
            if (SendMessageReturnBool(window, GetSelector("isKindOfClass:"), windowClass) &&
                SendMessageReturnBool(window, GetSelector("isVisible")))
            {
                return window;
            }
        }

        return IntPtr.Zero;
    }

    private static bool IsShowing(IntPtr view)
    {
        return !SendMessageReturnBool(view, GetSelector("isHiddenOrHasHiddenAncestor")) &&
            SendMessageReturnDouble(view, GetSelector("alphaValue")) > 0;
    }

    private static ControlBounds FrameInContent(IntPtr view, IntPtr window)
    {
        var contentView = SendMessage(window, GetSelector("contentView"));
        var viewBounds = SendMessageReturnCGRect(view, GetSelector("bounds"));
        var frame = SendMessageConvertRect(contentView, GetSelector("convertRect:fromView:"), viewBounds, view);
        var contentBounds = SendMessageReturnCGRect(contentView, GetSelector("bounds"));

        // AppKit measures up from a view's bottom edge unless the view is flipped.
        var top = frame.Y - contentBounds.Y;
        if (!SendMessageReturnBool(contentView, GetSelector("isFlipped")))
        {
            top = contentBounds.Y + contentBounds.Height - frame.Y - frame.Height;
        }

        return new ControlBounds(frame.X - contentBounds.X, top, frame.Width, frame.Height);
    }
}
