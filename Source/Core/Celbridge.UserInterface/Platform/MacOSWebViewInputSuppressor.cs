using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Stops hosted native WebViews taking mouse input while a managed overlay is open on the Uno Skia macOS
/// head. A WebView is a real native view above the canvas the overlay is drawn on, so AppKit hit-tests it
/// first and a click meant for a flyout reaches the page as well: the flyout renders correctly and acts on
/// the click, and the page acts on it too. Declining the hit test for as long as the overlay is open leaves
/// the page visible while the click falls through to the canvas, where Uno routes it to the overlay alone.
/// </summary>
internal static class MacOSWebViewInputSuppressor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string HostedWebViewClassName = "UNOWebView";

    [DllImport(LibObjC)]
    private static extern IntPtr class_getInstanceMethod(IntPtr classHandle, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr method_getImplementation(IntPtr method);

    [DllImport(LibObjC)]
    private static extern IntPtr method_setImplementation(IntPtr method, IntPtr implementation);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    private delegate IntPtr OriginalHitTest(IntPtr self, IntPtr selector, CGPoint point);

    // Marshalled once at startup rather than per call: hitTest: runs on every mouse move, for every hosted
    // web view.
    private static OriginalHitTest? _originalHitTest;
    private static Logging.ILogger? _logger;
    private static bool _started;

    // Counted rather than a flag: overlays can nest (a context menu inside a flyout), and the innermost
    // one closing must not re-enable input for the outer one.
    private static int _suppressionCount;

    public static unsafe void Start(Logging.ILogger logger)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (_started)
        {
            return;
        }

        var hostedWebViewClass = GetClass(HostedWebViewClassName);
        if (hostedWebViewClass == IntPtr.Zero)
        {
            logger.LogWarning(
                "Cannot suppress WebView input under overlays: Uno no longer registers a {ClassName} class",
                HostedWebViewClassName);
            return;
        }

        var method = class_getInstanceMethod(hostedWebViewClass, GetSelector("hitTest:"));
        if (method == IntPtr.Zero)
        {
            logger.LogWarning("Cannot suppress WebView input under overlays: hitTest: was not resolved");
            return;
        }

        _started = true;
        _logger = logger;

        // Captured before the hook is installed, so the hook can never run without something to call.
        _originalHitTest = Marshal.GetDelegateForFunctionPointer<OriginalHitTest>(
            method_getImplementation(method));

        var hook = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, CGPoint, IntPtr>)&HitTestHook;
        method_setImplementation(method, hook);
    }

    /// <summary>
    /// Makes hosted WebViews decline mouse hit tests until the returned scope is disposed. A no-op when the
    /// hook is not installed, so the caller needs no platform check.
    /// </summary>
    public static IDisposable Suppress()
    {
        return new SuppressionScope();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr HitTestHook(IntPtr self, IntPtr selector, CGPoint point)
    {
        // Runs inside AppKit's hit testing, on every mouse move, so the whole hook is a volatile read and a
        // call through a delegate marshalled at startup. Neither can throw into native code.
        if (Volatile.Read(ref _suppressionCount) > 0)
        {
            return IntPtr.Zero;
        }

        return _originalHitTest!(self, selector, point);
    }

    private sealed class SuppressionScope : IDisposable
    {
        private bool _disposed;

        public SuppressionScope()
        {
            Interlocked.Increment(ref _suppressionCount);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Interlocked.Decrement(ref _suppressionCount);
        }
    }
}
