using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celbridge.WebHost;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

// The Uno SDK's implicit global usings include System.Windows.Input, which also contains a FocusManager
// type, so the bare name is ambiguous.
using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Keeps a focused web surface's native first responder from being lost to managed-focus housekeeping on
/// the Skia head. Uno resigns the native first responder (making the bare content view first responder)
/// whenever its FocusManager applies focus to a managed element, including redundant re-asserts where
/// managed focus does not actually change, such as an Explorer item container recycling during a background
/// refresh. Such a resign deactivates the focused page: its caret hides and keystrokes beep. A real focus
/// move always raises FocusManager.GotFocus and a redundant re-assert does not, so the guard watches
/// makeFirstResponder: on Uno's window class and, when a resign to the content view is followed by no
/// managed focus change, reconciles focus, which restores the focused surface's native focus. macOS-only.
/// </summary>
internal static class MacOSNativeFocusGuard
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // Long enough for the GotFocus a real focus move raises to arrive first, short enough that the page's
    // caret barely blinks when a resign is undone.
    private static readonly TimeSpan ReassertDelay = TimeSpan.FromMilliseconds(150);

    [DllImport(LibObjC)]
    private static extern IntPtr class_getInstanceMethod(IntPtr classHandle, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr method_setImplementation(IntPtr method, IntPtr implementation);

    private delegate bool OriginalMakeFirstResponder(IntPtr self, IntPtr selector, IntPtr responder);

    private static IntPtr _originalImplementation;
    private static IFocusReconciler? _focusReconciler;
    private static Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private static Logging.ILogger? _logger;
    private static long _lastManagedFocusChangeAt;
    private static long _pendingResignAt;
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
            logger.LogWarning("Cannot guard native web-surface focus: the UNOWindow class is not registered");
            return;
        }

        var method = class_getInstanceMethod(windowClass, GetSelector("makeFirstResponder:"));
        if (method == IntPtr.Zero)
        {
            logger.LogWarning("Cannot guard native web-surface focus: makeFirstResponder: was not resolved");
            return;
        }

        _started = true;
        _focusReconciler = focusReconciler;
        _logger = logger;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // A real managed focus move raises GotFocus carrying the element that took focus. The housekeeping
        // re-asserts this guard undoes raise it with a null element (measured: an Explorer refresh resigns
        // the native first responder and reports GotFocus with no element), so only element-carrying events
        // count as a real handoff.
        FocusManager.GotFocus += (_, e) =>
        {
            if (e.NewFocusedElement is UIElement)
            {
                _lastManagedFocusChangeAt = Environment.TickCount64;
                return;
            }

            ReassertIfResignPending();
        };

        var hook = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)&MakeFirstResponderHook;
        _originalImplementation = method_setImplementation(method, hook);
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

        var original = Marshal.GetDelegateForFunctionPointer<OriginalMakeFirstResponder>(_originalImplementation);
        return original(self, selector, responder) ? (byte)1 : (byte)0;
    }

    // The focus application that carries no element is the housekeeping this guard undoes, and it arrives
    // with the resign rather than after it. Acting on it restores focus within a frame instead of waiting out
    // the confirmation delay, which the user sees as the caret blinking off and back on with every click.
    // Deferred rather than immediate because this runs inside Uno's own focus application.
    private static void ReassertIfResignPending()
    {
        var pendingResignAt = _pendingResignAt;
        if (pendingResignAt == 0
            || Environment.TickCount64 - pendingResignAt > ReassertDelay.TotalMilliseconds)
        {
            return;
        }

        _pendingResignAt = 0;

        _dispatcherQueue?.TryEnqueue(() =>
        {
            _logger?.LogDebug("Reasserted native web-surface focus on a housekeeping focus application");
            _focusReconciler?.Reconcile();
        });
    }

    private static void OnResignedToContentView()
    {
        var resignedAt = Environment.TickCount64;
        _pendingResignAt = resignedAt;

        var enqueued = _dispatcherQueue?.TryEnqueue(async () =>
        {
            await Task.Delay(ReassertDelay);

            // Already restored on the housekeeping focus application, or superseded by a later resign.
            if (_pendingResignAt != resignedAt)
            {
                return;
            }

            _pendingResignAt = 0;

            // A managed focus change since the resign means the resign was a real handoff (a dialog taking
            // the keyboard, a click on a managed panel) and must stand.
            if (_lastManagedFocusChangeAt >= resignedAt)
            {
                return;
            }

            // No focus change followed: the resign was housekeeping. Reconciling restores the focused
            // surface's native focus, or re-asserts the content view when no web surface holds focus.
            _logger?.LogDebug("Reasserted native web-surface focus after a housekeeping resign");
            _focusReconciler?.Reconcile();
        });

        if (enqueued != true)
        {
            _logger?.LogWarning("Could not schedule the native focus re-assert");
        }
    }
}
