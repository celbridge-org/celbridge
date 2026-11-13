using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Logging;
using Microsoft.UI.Dispatching;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Tells each web view when a click lands in it. One AppKit local mouse-down monitor serves every web view in the
/// process, and hit-tests each click against the native view hierarchy. macOS-only.
/// The monitor is the one signal that sees every click in a WKWebView. Managed GotFocus misses all of them. DOM focus
/// events miss clicks on content that cannot take focus, such as rendered markdown. Responder state misses them
/// too, because Uno keeps its Skia canvas (UNOMetalFlippedView) as the window's first responder.
/// </summary>
internal static class MacOSWebViewFocusMonitor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // NSEventMaskLeftMouseDown == 1 << 1, NSEventMaskRightMouseDown == 1 << 3.
    private const ulong EventMaskMouseDown = (1UL << 1) | (1UL << 3);

    // An NSPoint is two doubles, a homogeneous float aggregate the ARM64 ABI passes and returns in the
    // floating-point registers, so the struct marshals by value through plain objc_msgSend. The
    // struct-shaped signatures keep these declarations local rather than in the shared runtime.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern NSPoint SendMessageReturnNSPoint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageHitTest(IntPtr receiver, IntPtr selector, NSPoint point);

    // The AppKit monitor and its UnmanagedCallersOnly callback are process-global. All access happens on the main
    // thread. The monitor is installed when a web view is created, and its callback runs during AppKit event
    // dispatch.
    private static bool _isLastPressInWebView;
    private static bool _monitorInstalled;
    private static DispatcherQueue? _dispatcherQueue;
    private static ILogger? _logger;

    /// <summary>
    /// Whether the most recent mouse press landed in a web view. The monitor records the answer before the managed
    /// pointer pipeline raises that press.
    /// </summary>
    public static bool IsLastPressInWebView => _isLastPressInWebView;

    /// <summary>
    /// Installs the monitor, on the main thread, before any web view can be clicked. Later calls do nothing.
    /// </summary>
    public static unsafe void Install(ILogger logger)
    {
        if (_monitorInstalled)
        {
            return;
        }

        _monitorInstalled = true;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var handler = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&MonitorCallback;
        AddLocalEventMonitor(EventMaskMouseDown, handler);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr MonitorCallback(IntPtr block, IntPtr nsEvent)
    {
        // Runs on the main thread during event dispatch. Never let an exception cross back into AppKit.
        try
        {
            // Recorded for the managed copy of this press, which Uno raises after the monitor has run. Cleared
            // first, so a hit test that throws leaves no answer from an earlier press.
            _isLastPressInWebView = false;

            var clickedView = FindClickedWebView(nsEvent);
            _isLastPressInWebView = clickedView is not null;

            // Every click inside a web view is signalled, including a repeat click on the same view. Focus can
            // leave a surface with no click at all, such as through Tab, a shortcut or a programmatic move. The
            // next click must then bring the keyboard back. The focus registry decides whether a click changes
            // focus.
            if (clickedView is not null)
            {
                // Defer so the view's focus handlers run after AppKit finishes dispatching the click.
                _dispatcherQueue?.TryEnqueue(clickedView.OnClicked);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "The web view focus monitor callback failed");
        }

        // Pure observer: always pass the event through unchanged.
        return nsEvent;
    }

    private static MacOSWebView? FindClickedWebView(IntPtr nsEvent)
    {
        var window = SendMessage(nsEvent, GetSelector("window"));
        if (window == IntPtr.Zero)
        {
            return null;
        }

        var contentView = SendMessage(window, GetSelector("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return null;
        }

        // locationInWindow is in window coordinates, which match the content view's superview (the
        // frame view) for the content area, so the standard contentView hitTest works directly.
        var location = SendMessageReturnNSPoint(nsEvent, GetSelector("locationInWindow"));
        var hitView = SendMessageHitTest(contentView, GetSelector("hitTest:"), location);

        // A click inside a WKWebView hits one of the WKWebView's descendant views. Walk up from the hit view to
        // the first view that is a web view's native view.
        var view = hitView;
        while (view != IntPtr.Zero)
        {
            var webView = MacOSWebView.FromNativeHandle(view);
            if (webView is not null)
            {
                return webView;
            }
            view = SendMessage(view, GetSelector("superview"));
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X;
        public double Y;
    }
}
