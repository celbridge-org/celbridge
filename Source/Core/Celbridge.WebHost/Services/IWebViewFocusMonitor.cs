using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Reports when a pointer click gives a registered hosted web view native focus, invoking a per-view
/// callback the caller supplies at registration. It exists for the macOS Skia head, where a click inside a
/// WKWebView raises no managed WebView.GotFocus and (for external-URL .webview documents with no injected
/// script, or clicks on non-focusable content such as rendered markdown) no DOM focus event either, leaving
/// nothing else to notice the surface became active. The macOS implementation hit-tests each click through
/// an AppKit mouse-down monitor; heads where managed GotFocus already fires (Windows), or that have no
/// monitor yet (Linux, pending a WebKitGTK equivalent), use a no-op implementation.
/// </summary>
internal interface IWebViewFocusMonitor
{
    /// <summary>
    /// Registers a web view so onFocusSignal runs on the UI thread when a click gives its native
    /// surface focus. Registering the same web view again replaces the previous callback.
    /// </summary>
    void Register(CoreWebView2 coreWebView, Action onFocusSignal);

    /// <summary>
    /// Removes a previously registered web view. Safe to call for a web view that was never
    /// registered.
    /// </summary>
    void Unregister(CoreWebView2 coreWebView);
}
