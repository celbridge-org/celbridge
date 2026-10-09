using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Logging;
using Microsoft.UI.Dispatching;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Tells each web view when a click lands in it. A click inside a WKWebView raises no managed GotFocus, and a
/// click on content that cannot take focus, such as rendered markdown, raises no DOM focus event either. An AppKit
/// local mouse-down monitor hit-tests each click against the native view hierarchy instead. Hit-testing is the
/// discriminator because Uno keeps its Skia canvas (UNOMetalFlippedView) as the window's first responder even for
/// clicks that land inside a hosted WKWebView, so responder state cannot tell the two apart. One monitor serves
/// every web view in the process. macOS-only.
/// </summary>
internal static class MacOSWebViewFocusMonitor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // BLOCK_IS_GLOBAL marks a block literal as a global (never copied or freed) block.
    private const int BlockIsGlobal = 1 << 28;

    // NSEventMaskLeftMouseDown == 1 << 1, NSEventMaskRightMouseDown == 1 << 3.
    private const ulong EventMaskMouseDown = (1UL << 1) | (1UL << 3);

    // objc_msgSend for +addLocalMonitorForEventsMatchingMask:handler: (an NSUInteger mask then a block).
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageAddMonitor(IntPtr receiver, IntPtr selector, nuint mask, IntPtr block);

    // An NSPoint is two doubles, a homogeneous float aggregate the ARM64 ABI passes and returns in the
    // floating-point registers, so the struct marshals by value through plain objc_msgSend. The
    // struct-shaped signatures keep these declarations local rather than in the shared runtime.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern NSPoint SendMessageReturnNSPoint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageHitTest(IntPtr receiver, IntPtr selector, NSPoint point);

    [DllImport(LibSystem)]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static readonly IntPtr RtldDefault = new(-2);

    // The AppKit monitor and its UnmanagedCallersOnly callback are process-global. All access happens on the main
    // thread: the monitor is installed from web view creation, and its callback runs during AppKit event dispatch.
    private static bool _isLastPressInWebView;
    private static bool _monitorInstalled;
    private static IntPtr _monitor;
    private static IntPtr _monitorBlock;
    private static DispatcherQueue? _dispatcherQueue;
    private static ILogger? _logger;

    /// <summary>
    /// Whether the most recent mouse press landed in a web view. It is answered before the managed pointer
    /// pipeline raises that press.
    /// </summary>
    public static bool IsLastPressInWebView => _isLastPressInWebView;

    /// <summary>
    /// Installs the monitor, on the main thread, before any web view can be clicked. Later calls do nothing.
    /// </summary>
    public static void Install(ILogger logger)
    {
        if (_monitorInstalled)
        {
            return;
        }

        _monitorInstalled = true;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var nsEventClass = GetClass("NSEvent");
        var selector = GetSelector("addLocalMonitorForEventsMatchingMask:handler:");
        var block = EnsureMonitorBlock();

        var monitor = SendMessageAddMonitor(nsEventClass, selector, (nuint)EventMaskMouseDown, block);

        // Retain the monitor object for the process lifetime so the subscription survives.
        _monitor = SendMessage(monitor, GetSelector("retain"));
    }

    // The handler is an Objective-C block of shape NSEvent* (^)(NSEvent*). Built once as a no-capture
    // global block whose invoke pointer is a managed method.
    private static unsafe IntPtr EnsureMonitorBlock()
    {
        if (_monitorBlock != IntPtr.Zero)
        {
            return _monitorBlock;
        }

        var descriptor = new BlockDescriptor
        {
            Reserved = 0,
            Size = (nuint)Marshal.SizeOf<BlockLiteral>(),
        };
        var descriptorPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(descriptor, descriptorPointer, false);

        var blockIsa = dlsym(RtldDefault, "_NSConcreteGlobalBlock");
        var invoke = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&MonitorCallback;

        var block = new BlockLiteral
        {
            Isa = blockIsa,
            Flags = BlockIsGlobal,
            Reserved = 0,
            Invoke = invoke,
            Descriptor = descriptorPointer,
        };
        var blockPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(block, blockPointer, false);

        _monitorBlock = blockPointer;
        return blockPointer;
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

            // Every click inside a web view is signalled. Whether it is a change of focus is the focus
            // registry's to decide: focus can leave a surface with no click at all (a shortcut opening the
            // find bar, Tab, a programmatic move), and a monitor comparing this click against the last one
            // would stay silent on the click that brings the keyboard back.
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

        // A click inside a WKWebView hits one of its descendant views, so walk up from the hit view to the
        // first one that is a web view's native view.
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }
}
