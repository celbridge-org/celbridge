using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.WebHost;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Watches the AppKit first responder and reconciles focus whenever native key focus returns to the window,
/// on the Skia head. Uno resigns the first responder every time its FocusManager applies focus to a managed
/// element, including redundant re-asserts where managed focus does not actually change, such as an
/// Explorer item container recycling during a background refresh. Such a resign deactivates the focused
/// page: its caret hides and keystrokes beep, and nothing in the managed world observes it. Hooking
/// makeFirstResponder: on Uno's window class is the only way to see it; reconciling afterwards restores the
/// focused surface when the resign disagreed with the focus model, and does nothing when it agreed.
/// macOS-only.
/// </summary>
internal static class MacOSFirstResponderMonitor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)]
    private static extern IntPtr class_getInstanceMethod(IntPtr classHandle, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr method_getImplementation(IntPtr method);

    [DllImport(LibObjC)]
    private static extern IntPtr method_setImplementation(IntPtr method, IntPtr implementation);

    private delegate bool OriginalMakeFirstResponder(IntPtr self, IntPtr selector, IntPtr responder);

    private static OriginalMakeFirstResponder? _originalMakeFirstResponder;
    private static IFocusReconciler? _focusReconciler;
    private static Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private static Logging.ILogger? _logger;
    private static bool _started;

    public static unsafe void Start(IFocusReconciler focusReconciler, Logging.ILogger logger)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (_started)
        {
            return;
        }

        var windowClass = GetClass("UNOWindow");
        if (windowClass == IntPtr.Zero)
        {
            logger.LogWarning("Cannot watch the native first responder: the UNOWindow class is not registered");
            return;
        }

        var method = class_getInstanceMethod(windowClass, GetSelector("makeFirstResponder:"));
        if (method == IntPtr.Zero)
        {
            logger.LogWarning("Cannot watch the native first responder: makeFirstResponder: was not resolved");
            return;
        }

        _started = true;
        _focusReconciler = focusReconciler;
        _logger = logger;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Captured before the hook is installed, so the hook can never run without something to call.
        _originalMakeFirstResponder = Marshal.GetDelegateForFunctionPointer<OriginalMakeFirstResponder>(
            method_getImplementation(method));

        var hook = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)&MakeFirstResponderHook;
        method_setImplementation(method, hook);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte MakeFirstResponderHook(IntPtr self, IntPtr selector, IntPtr responder)
    {
        // Runs inside AppKit's focus handling. Never let an exception unwind into native code.
        try
        {
            var contentView = SendMessage(self, GetSelector("contentView"));
            if (responder != IntPtr.Zero
                && responder == contentView)
            {
                OnResignedToContentView();
            }
        }
        catch
        {
        }

        return _originalMakeFirstResponder!(self, selector, responder) ? (byte)1 : (byte)0;
    }

    private static void OnResignedToContentView()
    {
        // Queued below Uno's own focus work rather than merely deferred past this call. A resign arrives
        // before the focus change it accompanies has been reported: a click resigns the first responder
        // during AppKit's mouse handling, and Uno raises the matching GotFocus a step later, so a normal
        // priority callback lands in between and reads a model that still names the outgoing surface.
        // Reconciling at low priority reads the settled model instead.
        var enqueued = _dispatcherQueue?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _focusReconciler?.Reconcile());

        if (enqueued != true)
        {
            _logger?.LogWarning("Could not schedule the focus reconcile after a native first-responder resign");
        }
    }
}
