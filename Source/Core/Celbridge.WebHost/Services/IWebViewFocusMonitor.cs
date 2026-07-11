using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Native macOS click-focus signal for hosted web views: the macOS Skia head raises no WebView.GotFocus
/// for clicks inside a WKWebView, so an AppKit mouse-down monitor hit-tests each click against the
/// registered web views and reports focus. This is the macOS focus signal for hosted web content,
/// including external-URL .webview documents with no injected script and clicks on non-focusable content
/// (e.g. rendered markdown) that raise no DOM focus event. Non-macOS Skia heads use a no-op implementation
/// and have no click-focus signal yet (a WebKitGTK equivalent for Linux is a follow-up).
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
