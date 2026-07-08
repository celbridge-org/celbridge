using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Native macOS click-focus signal for hosted web views, supplementing (not replacing) the primary
/// path where editors and the console report focus over their JS host channel
/// (IHostInput.OnFocusReceived). That path is the general signal but cannot see two cases this
/// monitor covers: external-URL .webview documents with no injected script, and clicks on
/// non-focusable content (e.g. rendered markdown) that raise no DOM focus event. Non-macOS heads,
/// which rely on the JS path, use a no-op implementation.
/// </summary>
public interface IWebViewFocusMonitor
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
