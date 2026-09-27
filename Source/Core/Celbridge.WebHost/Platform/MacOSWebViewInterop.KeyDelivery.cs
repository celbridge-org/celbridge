using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Records which key-down event a web view last received. A key reaches a page by one of three routes. AppKit
/// delivers it to the web view as first responder, the web view takes it as a key equivalent, or the host
/// forwards a key that Uno's pipeline diverted. When a page leaves a key unhandled, WebKit sends it back
/// through the application, and it reaches the host's forwarding a second time. The host checks here before
/// forwarding, so a page never gets the same key twice.
/// </summary>
public static partial class MacOSWebViewInterop
{
    // Whether the hooks are installed. Used only on the main thread, like the timestamp below.
    private static bool _isObservingKeyDowns;

    // The timestamp of the last key-down event a web view received. The timestamp identifies the event,
    // because WebKit keeps it when it sends the event back.
    private static double _lastKeyDownTimestamp = double.NaN;

    // WKWebView's own implementations. The hooks call them.
    private static IntPtr _originalKeyDown;
    private static IntPtr _originalPerformKeyEquivalent;

    /// <summary>
    /// Starts recording each key-down event a web view receives, by any route. The hooks go on WKWebView once
    /// per process. Later calls do nothing.
    /// </summary>
    public static unsafe void ObserveKeyDownDelivery()
    {
        if (_isObservingKeyDowns)
        {
            return;
        }

        var webViewClass = GetClass("WKWebView");
        if (webViewClass == IntPtr.Zero)
        {
            return;
        }

        _isObservingKeyDowns = true;

        _originalKeyDown = HookMethod(
            webViewClass,
            "keyDown:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&KeyDownHook,
            "v@:@");

        _originalPerformKeyEquivalent = HookMethod(
            webViewClass,
            "performKeyEquivalent:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)&PerformKeyEquivalentHook,
            "c@:@");
    }

    /// <summary>
    /// Whether a web view has already received this key-down event.
    /// </summary>
    public static bool HasWebViewReceivedKeyDown(IntPtr keyEvent)
    {
        if (keyEvent == IntPtr.Zero)
        {
            return false;
        }

        return ReadEventTimestamp(keyEvent) == _lastKeyDownTimestamp;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void KeyDownHook(IntPtr self, IntPtr selector, IntPtr keyEvent)
    {
        // Never let an exception unwind into AppKit.
        try
        {
            if (keyEvent != IntPtr.Zero)
            {
                _lastKeyDownTimestamp = ReadEventTimestamp(keyEvent);
            }

            if (_originalKeyDown != IntPtr.Zero)
            {
                ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)_originalKeyDown)(self, selector, keyEvent);
            }
        }
        catch
        {
        }
    }

    // AppKit offers a chord to each view in the window. A web view that returns YES has passed the chord to
    // its page.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe byte PerformKeyEquivalentHook(IntPtr self, IntPtr selector, IntPtr keyEvent)
    {
        // Never let an exception unwind into AppKit.
        try
        {
            if (_originalPerformKeyEquivalent == IntPtr.Zero)
            {
                return 0;
            }

            var isTaken = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)_originalPerformKeyEquivalent)(
                self,
                selector,
                keyEvent);

            if (isTaken != 0 &&
                keyEvent != IntPtr.Zero)
            {
                _lastKeyDownTimestamp = ReadEventTimestamp(keyEvent);
            }

            return isTaken;
        }
        catch
        {
            return 0;
        }
    }

    private static double ReadEventTimestamp(IntPtr keyEvent)
    {
        return SendMessageReturnDouble(keyEvent, GetSelector("timestamp"));
    }
}
