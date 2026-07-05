using System.Runtime.InteropServices;
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
/// native-fullscreen state, and updates the window's first responder. macOS-only. Call on the main (UI)
/// thread, where AppKit is safe.
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

        SendMessageVoid(window, GetSelector("makeFirstResponder:"), contentView);
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
