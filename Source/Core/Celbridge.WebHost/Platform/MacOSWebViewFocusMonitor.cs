using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// macOS IWebViewFocusMonitor. Installs an AppKit local mouse-down monitor and hit-tests each click
/// against the registered WKWebViews' native view hierarchies. Hit-testing is the discriminator
/// because Uno keeps its Skia canvas (UNOMetalFlippedView) as the window's first responder even for
/// clicks that land inside a hosted WKWebView, so responder state cannot tell the two apart. The
/// whole click is handled on the native side, so this signal needs neither the managed GotFocus
/// event nor any script injected into the page. macOS-only.
/// </summary>
public class MacOSWebViewFocusMonitor : IWebViewFocusMonitor
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

    // The AppKit monitor and its UnmanagedCallersOnly callback are process-global, so the state is
    // static; DI creates a single instance. All access happens on the main thread: Register and
    // Unregister run from view lifecycle handlers and the monitor callback runs during AppKit event
    // dispatch.
    private static readonly Dictionary<IntPtr, Action> _callbacksByHandle = new();
    private static readonly Dictionary<CoreWebView2, IntPtr> _handlesByWebView = new();

    private static bool _monitorInstalled;
    private static IntPtr _monitor;
    private static IntPtr _monitorBlock;
    private static IntPtr _lastMatchedHandle;
    private static DispatcherQueue? _dispatcherQueue;
    private static ILogger? _logger;

    public MacOSWebViewFocusMonitor(ILogger<MacOSWebViewFocusMonitor> logger)
    {
        _logger = logger;
    }

    public void Register(CoreWebView2 coreWebView, Action onFocusSignal)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView, out var handle, out var detail))
        {
            _logger?.LogDebug($"Could not resolve the native web view for focus monitoring: {detail}");
            return;
        }

        // Re-registration replaces the previous entry, including a stale handle entry if the web
        // view's native view was recreated since the last registration.
        if (_handlesByWebView.TryGetValue(coreWebView, out var previousHandle)
            && previousHandle != handle)
        {
            _callbacksByHandle.Remove(previousHandle);
        }

        _handlesByWebView[coreWebView] = handle;
        _callbacksByHandle[handle] = onFocusSignal;

        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();
        EnsureMonitorInstalled();
    }

    public void Unregister(CoreWebView2 coreWebView)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!_handlesByWebView.Remove(coreWebView, out var handle))
        {
            return;
        }

        _callbacksByHandle.Remove(handle);

        // Forget the dedup state for the removed view so a pooled web view that is reacquired for a
        // new document reports its first click.
        if (_lastMatchedHandle == handle)
        {
            _lastMatchedHandle = IntPtr.Zero;
        }
    }

    private static void EnsureMonitorInstalled()
    {
        if (_monitorInstalled)
        {
            return;
        }

        _monitorInstalled = true;

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
            var matchedHandle = FindClickedRegisteredWebView(nsEvent);

            // Report transitions only: repeated clicks inside the same web view stay quiet, and a click
            // that lands anywhere else resets the state so returning to the view reports again.
            if (matchedHandle != _lastMatchedHandle)
            {
                _lastMatchedHandle = matchedHandle;

                if (matchedHandle != IntPtr.Zero
                    && _callbacksByHandle.TryGetValue(matchedHandle, out var callback))
                {
                    // Defer so the callback's UI work runs after AppKit finishes dispatching the click.
                    _dispatcherQueue?.TryEnqueue(() => callback());
                }
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "The web view focus monitor callback failed");
        }

        // Pure observer: always pass the event through unchanged.
        return nsEvent;
    }

    private static IntPtr FindClickedRegisteredWebView(IntPtr nsEvent)
    {
        var window = SendMessage(nsEvent, GetSelector("window"));
        if (window == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var contentView = SendMessage(window, GetSelector("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // locationInWindow is in window coordinates, which match the content view's superview (the
        // frame view) for the content area, so the standard contentView hitTest works directly.
        var location = SendMessageReturnNSPoint(nsEvent, GetSelector("locationInWindow"));
        var hitView = SendMessageHitTest(contentView, GetSelector("hitTest:"), location);

        // A click inside a WKWebView hits one of its descendant views, so walk up from the hit view
        // comparing against the registered web view handles.
        var view = hitView;
        while (view != IntPtr.Zero)
        {
            if (_callbacksByHandle.ContainsKey(view))
            {
                return view;
            }
            view = SendMessage(view, GetSelector("superview"));
        }

        return IntPtr.Zero;
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
