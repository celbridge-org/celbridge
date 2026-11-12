using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Defers a page's in-window change while Uno has the page's web view out of the window. Uno takes a web view out of
/// the window whenever the control leaves the visual tree, as when a document goes to a background tab. Uno puts the
/// web view back when the control returns. Between the two, the page keeps its in-window state. The page still
/// receives the visibility change. WebKit sends that change on its next run loop pass, and returns from adding the
/// web view back before the page repaints. A script that reads the page straight after a tab switch can therefore
/// find the page still hidden.
/// </summary>
public static partial class MacOSWebViewInterop
{
    // Set to true once the hooks are installed. Used only on the main thread, like the set below.
    private static bool _isDeferringInWindowChanges;

    // The web views deferring an in-window change, by native handle. AppKit calls the hooks on the main thread.
    private static readonly HashSet<IntPtr> _deferringWebViews = [];

    // The implementations UNOWebView had before the hooks went on. Each hook calls the one it replaced.
    private static IntPtr _originalRemoveFromSuperview;
    private static IntPtr _originalViewDidMoveToWindow;

    private static readonly IntPtr BeginDeferringSelector = GetSelector("_beginDeferringViewInWindowChanges");
    private static readonly IntPtr EndDeferringSelector = GetSelector("_endDeferringViewInWindowChanges");

    /// <summary>
    /// Installs hooks that defer each web view's in-window change from the moment the web view leaves the window until
    /// the web view is back in one. Returns true when the hooks are installed, or were already. Returns false when
    /// Uno's web view class or a WebKit deferral method is missing, and leaves the class as it was.
    /// </summary>
    // WEBKIT-BUG: https://bugs.webkit.org/show_bug.cgi?id=330068. When a page leaves the window, WebKit drops the
    // scrolling nodes of every frame in the page. When the page returns to a window, WebKit rebuilds those nodes with
    // their scroll bars disabled. A scroll bar inside a frame then draws no thumb. The page's own scroll bar keeps
    // working. WebKit fixed this on main in November 2026.
    public static unsafe bool DeferInWindowChanges()
    {
        if (_isDeferringInWindowChanges)
        {
            return true;
        }

        var webViewClass = GetClass("UNOWebView");
        if (webViewClass == IntPtr.Zero ||
            class_getInstanceMethod(webViewClass, BeginDeferringSelector) == IntPtr.Zero ||
            class_getInstanceMethod(webViewClass, EndDeferringSelector) == IntPtr.Zero ||
            class_getInstanceMethod(webViewClass, GetSelector("removeFromSuperview")) == IntPtr.Zero ||
            class_getInstanceMethod(webViewClass, GetSelector("viewDidMoveToWindow")) == IntPtr.Zero)
        {
            return false;
        }

        _isDeferringInWindowChanges = true;

        _originalRemoveFromSuperview = HookMethod(
            webViewClass,
            "removeFromSuperview",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&RemoveFromSuperviewHook,
            "v@:");

        _originalViewDidMoveToWindow = HookMethod(
            webViewClass,
            "viewDidMoveToWindow",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&ViewDidMoveToWindowHook,
            "v@:");

        return true;
    }

    // Uno's detach and Uno's dispose both call removeFromSuperview, the dispose through the dispose hook. A web view in
    // a window leaves the window here, so the deferral starts before WebKit learns of the move.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void RemoveFromSuperviewHook(IntPtr self, IntPtr selector)
    {
        // Never let an exception unwind into AppKit.
        try
        {
            if (HasWindow(self) &&
                _deferringWebViews.Add(self))
            {
                SendMessage(self, BeginDeferringSelector);
            }

            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)_originalRemoveFromSuperview)(self, selector);
        }
        catch
        {
        }
    }

    // Ending the deferral sends an in-window change only when the web view's state differs from the page's. A web view
    // back in a window matches the page, so the page receives no in-window change at all.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void ViewDidMoveToWindowHook(IntPtr self, IntPtr selector)
    {
        // Never let an exception unwind into AppKit.
        try
        {
            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)_originalViewDidMoveToWindow)(self, selector);

            if (HasWindow(self) &&
                _deferringWebViews.Remove(self))
            {
                SendMessage(self, EndDeferringSelector);
            }
        }
        catch
        {
        }
    }
}
