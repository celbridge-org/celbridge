#if !WINDOWS
using System.Runtime.InteropServices;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The geometry of one display, as reported by NSScreen. The frame is in macOS points with a
/// bottom-left origin; BackingScaleFactor converts points to physical pixels for that display.
/// </summary>
internal readonly record struct MacScreen(
    double FrameX,
    double FrameY,
    double FrameWidth,
    double FrameHeight,
    double BackingScaleFactor);

/// <summary>
/// Objective-C interop for the native AppKit window and screens behind Uno's macOS Skia head. Uno does
/// not surface the NSWindow, and DisplayArea is unimplemented on the Skia head, so the app reaches AppKit
/// directly to read display geometry (for validating a saved window placement) and to ask whether the
/// window is in native fullscreen. macOS owns fullscreen itself (the title-bar green button), so this
/// only reads state; it does not drive the window. Reports failure on other platforms.
/// </summary>
/// <remarks>
/// AppKit is only safe on the macOS main (UI) thread, so callers must invoke these there. The type is
/// macOS-only and callers gate on OperatingSystem.IsMacOS(). Unlike MacOSWebViewInterop it uses no Uno
/// reflection (it messages NSApplication and NSScreen, which are stable system classes), so it needs no
/// re-verification on an Uno bump.
/// </remarks>
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

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageIndex(IntPtr receiver, IntPtr selector, nint index);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendMessageReturnNint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendMessageReturnNuint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern double SendMessageReturnDouble(IntPtr receiver, IntPtr selector);

    // NSRect is a homogeneous aggregate of four doubles, so the ARM64 ABI returns it in the floating
    // point registers; the CGRect struct marshals directly.
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

        var screenClass = objc_getClass("NSScreen");
        if (screenClass == IntPtr.Zero)
        {
            return false;
        }

        var screenArray = SendMessage(screenClass, sel_registerName("screens"));
        if (screenArray == IntPtr.Zero)
        {
            return false;
        }

        var count = SendMessageReturnNint(screenArray, sel_registerName("count"));
        if (count <= 0)
        {
            return false;
        }

        var objectAtIndexSelector = sel_registerName("objectAtIndex:");
        var frameSelector = sel_registerName("frame");
        var backingScaleSelector = sel_registerName("backingScaleFactor");

        var result = new List<MacScreen>((int)count);
        for (nint index = 0; index < count; index++)
        {
            var screen = SendMessageIndex(screenArray, objectAtIndexSelector, index);
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

        var styleMask = SendMessageReturnNuint(window, sel_registerName("styleMask"));
        return (styleMask & NSWindowStyleMaskFullScreen) != 0;
    }

    private static IntPtr GetMainWindow()
    {
        var application = SendMessage(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        if (application == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var mainWindow = SendMessage(application, sel_registerName("mainWindow"));
        if (mainWindow != IntPtr.Zero)
        {
            return mainWindow;
        }

        // mainWindow is nil when the app is not the active application, so fall back to the first window
        // in the application's window list (Celbridge is single-window).
        var windows = SendMessage(application, sel_registerName("windows"));
        if (windows == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var count = SendMessageReturnNint(windows, sel_registerName("count"));
        if (count <= 0)
        {
            return IntPtr.Zero;
        }

        return SendMessageIndex(windows, sel_registerName("objectAtIndex:"), 0);
    }
}
#endif
