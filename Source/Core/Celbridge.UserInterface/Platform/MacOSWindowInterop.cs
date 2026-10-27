using System.Runtime.InteropServices;
using Windows.Graphics;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// The geometry of one display, as reported by NSScreen. The frame is in macOS points with a
/// bottom-left origin. BackingScaleFactor converts points to physical pixels for that display.
/// </summary>
internal readonly record struct MacScreen(
    double FrameX,
    double FrameY,
    double FrameWidth,
    double FrameHeight,
    double BackingScaleFactor);

/// <summary>
/// Objective-C interop for the native AppKit window and screens behind Uno's macOS Skia head, which
/// surfaces neither the NSWindow nor (on the Skia head) the DisplayArea APIs. Reads display geometry and
/// native-fullscreen state, moves the window into and out of native fullscreen, places the window, sets the window's
/// minimum size, and updates the window's first responder.
/// macOS-only. Call on the main (UI) thread, where AppKit is safe.
/// </summary>
internal static class MacOSWindowInterop
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // NSWindowStyleMaskFullScreen (1 << 14) is set on a window that has entered native fullscreen.
    private const nuint NSWindowStyleMaskFullScreen = 1 << 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }

    // NSRect is a homogeneous aggregate of four doubles, so the ARM64 ABI returns it in the floating
    // point registers. The CGRect struct marshals directly. The struct-by-value return keeps this
    // declaration local rather than in the shared runtime.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGRect SendMessageReturnCGRect(IntPtr receiver, IntPtr selector);

    // NSSize is a pair of doubles, so the ARM64 ABI passes it in the floating point registers and the
    // CGSize struct marshals directly.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidCGSize(IntPtr receiver, IntPtr selector, CGSize argument);

    // The NSRect argument travels in the floating point registers. The BOOL after the NSRect marshals as one
    // byte.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidCGRectBool(
        IntPtr receiver,
        IntPtr selector,
        CGRect rect,
        [MarshalAs(UnmanagedType.I1)] bool argument);

    /// <summary>
    /// Reads the geometry of every attached display from NSScreen. Returns false off macOS or when the
    /// screen list cannot be read.
    /// </summary>
    public static bool TryGetScreens(out IReadOnlyList<MacScreen> screens)
    {
        screens = Array.Empty<MacScreen>();

        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var screenClass = GetClass("NSScreen");
        if (screenClass == IntPtr.Zero)
        {
            return false;
        }

        var screenArray = SendMessage(screenClass, GetSelector("screens"));
        if (screenArray == IntPtr.Zero)
        {
            return false;
        }

        var count = SendMessageReturnNint(screenArray, GetSelector("count"));
        if (count <= 0)
        {
            return false;
        }

        var objectAtIndexSelector = GetSelector("objectAtIndex:");
        var frameSelector = GetSelector("frame");
        var backingScaleSelector = GetSelector("backingScaleFactor");

        var result = new List<MacScreen>((int)count);
        for (nint index = 0; index < count; index++)
        {
            var screen = SendMessage(screenArray, objectAtIndexSelector, index);
            if (screen == IntPtr.Zero)
            {
                continue;
            }

            var frame = SendMessageReturnCGRect(screen, frameSelector);
            var backingScaleFactor = SendMessageReturnDouble(screen, backingScaleSelector);

            var macScreen = new MacScreen(
                frame.Origin.X,
                frame.Origin.Y,
                frame.Size.Width,
                frame.Size.Height,
                backingScaleFactor);
            result.Add(macScreen);
        }

        if (result.Count == 0)
        {
            return false;
        }

        screens = result;
        return true;
    }

    /// <summary>
    /// Sets the smallest content size the window can be resized to, in macOS points. AppKit enforces this
    /// for every user-driven resize, including the title-bar zoom button. No-op off macOS or when the
    /// window cannot be resolved.
    /// </summary>
    public static void SetMinimumContentSize(double width, double height)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            return;
        }

        var minimumSize = new CGSize
        {
            Width = width,
            Height = height
        };

        SendMessageVoidCGSize(window, GetSelector("setContentMinSize:"), minimumSize);
    }

    /// <summary>
    /// Reads the work area of the screen that contains the window. The work area leaves out the menu bar and the
    /// Dock. Falls back to the main screen while the window is on no screen. The rect is in macOS points with a
    /// top-left origin on the primary screen. Returns false off macOS or when no screen can be resolved.
    /// </summary>
    public static bool TryGetMainWindowWorkArea(out RectInt32 workArea)
    {
        workArea = default;

        if (!OperatingSystem.IsMacOS()
            || !TryGetPrimaryScreenHeight(out var primaryHeight))
        {
            return false;
        }

        var window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var screen = SendMessage(window, GetSelector("screen"));
        if (screen == IntPtr.Zero)
        {
            screen = SendMessage(GetClass("NSScreen"), GetSelector("mainScreen"));
        }

        if (screen == IntPtr.Zero)
        {
            return false;
        }

        workArea = ToTopLeft(SendMessageReturnCGRect(screen, GetSelector("visibleFrame")), primaryHeight);
        return workArea.Width > 0 && workArea.Height > 0;
    }

    /// <summary>
    /// Reads the work area of every attached screen. Each work area leaves out the menu bar and the Dock. The
    /// rects are in macOS points with a top-left origin on the primary screen. Returns false off macOS or when
    /// the screen list cannot be read.
    /// </summary>
    public static bool TryGetWorkAreas(out IReadOnlyList<RectInt32> workAreas)
    {
        workAreas = Array.Empty<RectInt32>();

        if (!OperatingSystem.IsMacOS()
            || !TryGetPrimaryScreenHeight(out var primaryHeight))
        {
            return false;
        }

        var screenArray = SendMessage(GetClass("NSScreen"), GetSelector("screens"));
        var count = SendMessageReturnNint(screenArray, GetSelector("count"));

        var objectAtIndexSelector = GetSelector("objectAtIndex:");
        var visibleFrameSelector = GetSelector("visibleFrame");

        var result = new List<RectInt32>((int)count);
        for (nint index = 0; index < count; index++)
        {
            var screen = SendMessage(screenArray, objectAtIndexSelector, index);
            if (screen == IntPtr.Zero)
            {
                continue;
            }

            result.Add(ToTopLeft(SendMessageReturnCGRect(screen, visibleFrameSelector), primaryHeight));
        }

        workAreas = result;
        return result.Count > 0;
    }

    /// <summary>
    /// Reads the window's frame, title bar included. The rect is in macOS points with a top-left origin on the
    /// primary screen. Returns false off macOS or when the window or the screens cannot be resolved.
    /// </summary>
    public static bool TryGetMainWindowFrame(out RectInt32 frame)
    {
        frame = default;

        if (!OperatingSystem.IsMacOS()
            || !TryGetPrimaryScreenHeight(out var primaryHeight))
        {
            return false;
        }

        var window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        frame = ToTopLeft(SendMessageReturnCGRect(window, GetSelector("frame")), primaryHeight);
        return frame.Width > 0 && frame.Height > 0;
    }

    /// <summary>
    /// Moves and resizes the window to a frame, title bar included. The rect is in macOS points with a top-left
    /// origin on the primary screen. Acts on macOS only, once the window and the screens resolve.
    /// </summary>
    public static void SetMainWindowFrame(RectInt32 frame)
    {
        if (!OperatingSystem.IsMacOS()
            || !TryGetPrimaryScreenHeight(out var primaryHeight))
        {
            return;
        }

        var window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            return;
        }

        var rect = new CGRect
        {
            Origin = new CGPoint
            {
                X = frame.X,
                Y = primaryHeight - (frame.Y + frame.Height)
            },
            Size = new CGSize
            {
                Width = frame.Width,
                Height = frame.Height
            }
        };

        SendMessageVoidCGRectBool(window, GetSelector("setFrame:display:"), rect, true);
    }

    // AppKit measures screens and windows up from the primary screen's bottom edge. The primary screen comes
    // first in the screen list, and it contains the menu bar.
    private static bool TryGetPrimaryScreenHeight(out double height)
    {
        height = 0;

        var screenArray = SendMessage(GetClass("NSScreen"), GetSelector("screens"));
        if (screenArray == IntPtr.Zero
            || SendMessageReturnNint(screenArray, GetSelector("count")) <= 0)
        {
            return false;
        }

        var primary = SendMessage(screenArray, GetSelector("objectAtIndex:"), (nint)0);
        if (primary == IntPtr.Zero)
        {
            return false;
        }

        height = SendMessageReturnCGRect(primary, GetSelector("frame")).Size.Height;
        return height > 0;
    }

    private static RectInt32 ToTopLeft(CGRect rect, double primaryHeight)
    {
        return new RectInt32
        {
            X = (int)Math.Round(rect.Origin.X),
            Y = (int)Math.Round(primaryHeight - (rect.Origin.Y + rect.Size.Height)),
            Width = (int)Math.Round(rect.Size.Width),
            Height = (int)Math.Round(rect.Size.Height)
        };
    }

    /// <summary>
    /// Whether the application window is currently in native macOS fullscreen (entered via the title-bar
    /// green button or the standard shortcut). Returns false off macOS or when the window cannot be
    /// resolved.
    /// </summary>
    public static bool IsMainWindowInNativeFullScreen()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var styleMask = SendMessageReturnNuint(window, GetSelector("styleMask"));
        return (styleMask & NSWindowStyleMaskFullScreen) != 0;
    }

    /// <summary>
    /// Moves the application window into native macOS fullscreen, or out of it. The window animates the change.
    /// The call returns at once off macOS, or when the window is missing.
    /// </summary>
    public static void ToggleMainWindowFullScreen()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            return;
        }

        SendMessageVoid(window, GetSelector("toggleFullScreen:"), IntPtr.Zero);
    }

    /// <summary>
    /// Makes the main window's content view the AppKit first responder, resigning any hosted WebView that
    /// was holding it. Called when a managed Uno panel gains focus so the native Edit-menu shortcuts
    /// (cut:/copy:/paste:/undo:/redo:) disable for that panel and the key equivalents fall through to Uno's
    /// own keyboard handling, instead of routing to a stale WebView. No-op off macOS.
    /// </summary>
    public static void MakeContentViewFirstResponder()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var window = GetMainWindow();
        if (window == IntPtr.Zero)
        {
            return;
        }

        var contentView = SendMessage(window, GetSelector("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return;
        }

        // Re-applying this when the content view already holds first responder would resign and
        // re-establish it, which the native focus guard observes as a fresh resign and reconciles in
        // response, looping.
        var firstResponder = SendMessage(window, GetSelector("firstResponder"));
        if (firstResponder == contentView)
        {
            return;
        }

        SendMessageVoid(window, GetSelector("makeFirstResponder:"), contentView);
    }

    /// <summary>
    /// Whether the application window holds the keyboard. False while a native panel such as a file picker
    /// runs in front of it, and false off macOS.
    /// </summary>
    public static bool IsAppWindowKey()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return false;
        }

        var keyWindow = SendMessage(application, GetSelector("keyWindow"));
        if (keyWindow == IntPtr.Zero)
        {
            return false;
        }

        // A panel takes the keyboard without becoming the main window, so the two windows differ while one is
        // up. AppKit can also leave mainWindow nil then.
        var mainWindow = SendMessage(application, GetSelector("mainWindow"));
        if (keyWindow == mainWindow)
        {
            return true;
        }

        // Live windows are KVO-swizzled subclasses of Uno's window class, so the class is matched rather
        // than the pointer.
        var windowClass = GetClass("UNOWindow");
        if (windowClass == IntPtr.Zero)
        {
            return false;
        }

        return SendMessageReturnBool(keyWindow, GetSelector("isKindOfClass:"), windowClass);
    }

    /// <summary>
    /// Returns the native NSWindow handle for the application window, or IntPtr.Zero when it cannot be
    /// resolved.
    /// </summary>
    internal static IntPtr GetMainWindow()
    {
        var application = SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var mainWindow = SendMessage(application, GetSelector("mainWindow"));
        if (mainWindow != IntPtr.Zero)
        {
            return mainWindow;
        }

        // mainWindow is nil when the app is not the active application, so fall back to the first window
        // in the application's window list (Celbridge is single-window).
        var windows = SendMessage(application, GetSelector("windows"));
        if (windows == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var count = SendMessageReturnNint(windows, GetSelector("count"));
        if (count <= 0)
        {
            return IntPtr.Zero;
        }

        return SendMessage(windows, GetSelector("objectAtIndex:"), (nint)0);
    }
}
