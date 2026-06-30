using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Per-platform WebView2 operations for the document editor stack. The packaged Windows head drives the
/// WebView2 SDK directly. The Uno Skia heads fall back to ExecuteScriptAsync where the managed surface is
/// unimplemented and, on macOS, to the native WKWebView interop. Selecting the implementation in DI keeps the
/// editor views and host plumbing free of platform branching.
/// </summary>
public interface IWebViewAdapter
{
    /// <summary>
    /// True when the WebView2 is a native WKWebView that loses its context if re-parented, so a view must
    /// create its control in place rather than reuse a pre-warmed, re-parentable pool.
    /// </summary>
    bool CreatesWebViewInPlace { get; }

    /// <summary>
    /// True when the platform does not destroy the WebView on Close(), so a loaded page keeps running its
    /// scripts after the document closes and must be navigated to about:blank before teardown.
    /// </summary>
    bool RequiresPageUnloadBeforeClose { get; }

    /// <summary>
    /// True when the platform benefits from a background-warmed pool of WebView2 controls. Only the packaged
    /// Windows head initializes controls while detached, which the pre-warm relies on.
    /// </summary>
    bool UsesPrewarmedPool { get; }

    /// <summary>
    /// True when the platform can map a virtual host name to a local folder (a real https origin). The Skia
    /// heads cannot, and load content under a faked origin via LoadHtmlString instead.
    /// </summary>
    bool SupportsVirtualHostMapping { get; }

    /// <summary>
    /// Brings a detached WebView2's CoreWebView2 to life. On the Skia heads this parents the control in a
    /// hidden, window-rooted host for the duration of initialization, which EnsureCoreWebView2Async requires.
    /// </summary>
    Task EnsureCoreWebView2Async(WebView2 webView);

    /// <summary>
    /// Removes the WebView from its container and closes it. On macOS this also calls the native WKWebView
    /// teardown SPI, which the managed Close() does not reach on the Skia head.
    /// </summary>
    void CloseWebView(WebView2 webView, Panel container);

    /// <summary>
    /// Evaluates a JavaScript expression and returns the JSON-encoded result. On the Skia heads common
    /// WKWebView eval faults (script errors, undefined results) are normalized to "null".
    /// </summary>
    Task<string> EvalAsync(CoreWebView2 coreWebView2, string expression);

    /// <summary>
    /// Reloads the page, optionally clearing the HTTP cache first. Cache clearing is best-effort on the Skia
    /// heads, which reload through the page rather than the unimplemented CoreWebView2.Reload.
    /// </summary>
    Task ReloadAsync(CoreWebView2 coreWebView2, bool clearCache);

    /// <summary>
    /// Captures the rendered surface to encoded image bytes. Uses the Chrome DevTools Protocol on Windows and
    /// the native WKWebView snapshot on macOS. Throws when the surface cannot be captured.
    /// </summary>
    Task<ScreenshotData> CaptureScreenshotAsync(WebView2 webView, ScreenshotRequest request);

    /// <summary>
    /// Posts a host-to-page message. Uses CoreWebView2 web messaging on Windows. On the Skia heads, where that
    /// direction is unimplemented, it invokes the client's receive function via ExecuteScriptAsync.
    /// </summary>
    void PostMessageToWeb(CoreWebView2 coreWebView2, string json);

    /// <summary>
    /// Installs a script that runs at document-start on every navigation, before page scripts. Uses the managed
    /// document-start API on Windows and a native WKUserScript on macOS.
    /// </summary>
    Task InstallDocumentStartScriptAsync(CoreWebView2 coreWebView2, string script);

    /// <summary>
    /// Re-delivers a document-start script after a navigation completes. A no-op on Windows, where the managed
    /// document-start script persists across navigations. On the Skia heads it re-runs the script.
    /// </summary>
    Task ReinjectDocumentStartScriptAsync(CoreWebView2 coreWebView2, string script);

    /// <summary>
    /// Loads an HTML string so the document reports the given base URL as its origin. The macOS replacement for
    /// virtual-host mapping, used on the Skia heads where SupportsVirtualHostMapping is false.
    /// </summary>
    void LoadHtmlString(CoreWebView2 coreWebView2, string html, string baseUrl);
}
