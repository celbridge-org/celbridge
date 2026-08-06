using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Options for a whole-page find session. Find always wraps, matching browser behaviour.
/// OnMatchStateChanged is invoked on the UI thread as the find advances, so the host bar can reflect state.
/// </summary>
public sealed record FindOptions(
    bool CaseSensitive = false,
    Action<FindMatchState>? OnMatchStateChanged = null);

/// <summary>
/// A snapshot of find progress. MatchFound drives next/previous enable state. MatchCount and ActiveMatchIndex
/// (1-based) are null unless a backend supplies a free match total. The macOS findString backend reports
/// presence only and leaves them null.
/// </summary>
public sealed record FindMatchState(bool MatchFound, int? MatchCount = null, int? ActiveMatchIndex = null);

/// <summary>
/// Per-platform WebView2 operations for the document editor stack. The packaged Windows head drives the
/// WebView2 SDK directly. The Uno Skia heads fall back to ExecuteScriptAsync where the managed surface is
/// unimplemented and, on macOS, to the native WKWebView interop. Selecting the implementation in DI keeps the
/// editor views and host plumbing free of platform branching.
/// </summary>
public interface IWebViewAdapter
{
    /// <summary>
    /// True when the platform can map a virtual host name to a local folder and serve it under a faked origin.
    /// True on the packaged Windows head and the Windows Skia head (both back a real WebView2). False on the
    /// macOS and Linux Skia heads, which fake the origin via LoadHtmlString instead.
    /// </summary>
    bool SupportsVirtualHostMapping { get; }

    /// <summary>
    /// True when the hosted WebView backend supplies its own find bar, so the host does not add one and the
    /// StartFindAsync/FindNext/FindPrevious/StopFind methods are inert. True on the Windows heads (Chromium's
    /// WebView2 has a built-in bar reached directly by Ctrl+F). False on the macOS and Linux Skia heads, whose
    /// WKWebView and WebKitGTK backends have none, so the host find bar drives find through this adapter.
    /// </summary>
    bool ProvidesBuiltInFind { get; }

    /// <summary>
    /// True when browsing data can be cleared while the application runs, taking effect immediately and
    /// without closing anything. True on the packaged Windows head, through CoreWebView2.Profile, and on the
    /// macOS Skia head, through the native WKWebsiteDataStore. False on the Windows and Linux Skia heads,
    /// which have neither.
    /// </summary>
    bool SupportsLiveBrowsingDataClear { get; }

    /// <summary>
    /// True when ClearBrowsingDataAsync needs a live WebView to reach the shared store through. True on the
    /// packaged Windows head, where the profile hangs off a CoreWebView2. False on macOS, where the default
    /// WKWebsiteDataStore is process-wide and reachable with no instance.
    /// </summary>
    bool BrowsingDataClearRequiresInstance { get; }

    /// <summary>
    /// Brings a detached WebView2's CoreWebView2 to life. On the Skia heads this parents the control in a
    /// hidden, window-rooted host for the duration of initialization, which EnsureCoreWebView2Async requires.
    /// </summary>
    Task EnsureCoreWebView2Async(WebView2 webView);

    /// <summary>
    /// Removes the WebView from its container and closes it. Pass a null container for a WebView that was
    /// never parented, such as one taken straight from the pool. On macOS this also calls the native
    /// WKWebView teardown SPI, which the managed Close() does not reach on the Skia head.
    /// </summary>
    void CloseWebView(WebView2 webView, Panel? container);

    /// <summary>
    /// Gives the hosted web content keyboard focus, reproducing what a click inside the view establishes.
    /// Managed focus does this on the Windows heads. On the macOS Skia head managed focus routes keys
    /// through the managed pipeline, where they never reach the web content, so the native WKWebView is
    /// made the window's first responder instead, and managed focus is moved off the control that held it
    /// so that control stops acting on the keys the pipeline still routes to it.
    /// </summary>
    void FocusWebView(WebView2 webView);

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
    /// Stops the navigation in progress. Uses CoreWebView2.Stop on the packaged Windows head. That member is
    /// unimplemented on the Skia heads, so macOS stops through the native WKWebView and the remaining heads
    /// fall back to window.stop(), which cannot stop a load that has not yet produced a document.
    /// </summary>
    Task StopAsync(CoreWebView2 coreWebView2);

    /// <summary>
    /// Clears the cookies, cached credentials, site data and HTTP cache of the store every WebView in the
    /// application shares, so the clear applies to all of them. Takes a live instance only where
    /// BrowsingDataClearRequiresInstance says one is needed, and is a no-op where
    /// SupportsLiveBrowsingDataClear is false. Throws if the clear does not complete.
    /// </summary>
    Task ClearBrowsingDataAsync(CoreWebView2? coreWebView2);

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
    /// Re-delivers a document-start script after a navigation completes. A no-op on the packaged Windows head,
    /// where the managed document-start script persists across navigations. On the Skia heads it re-runs the
    /// script.
    /// </summary>
    Task ReinjectDocumentStartScriptAsync(CoreWebView2 coreWebView2, string script);

    /// <summary>
    /// Loads an HTML string so the document reports the given base URL as its origin. The replacement for
    /// virtual-host mapping on the macOS and Linux Skia heads, where SupportsVirtualHostMapping is false.
    /// </summary>
    void LoadHtmlString(CoreWebView2 coreWebView2, string html, string baseUrl);

    /// <summary>
    /// Sets the WebView's User-Agent to a browser-recognised string that also identifies the application by the
    /// given token (e.g. "Celbridge/0.3.0"). The Skia macOS head's default WKWebView UA omits the Safari token
    /// some sites sniff for and flag as unsupported, so it is replaced with a Safari-compatible UA carrying the
    /// token. The Windows head appends the token to its already-recognised UA. Must be set before navigation.
    /// </summary>
    void SetApplicationUserAgent(CoreWebView2 coreWebView2, string applicationToken);

    /// <summary>
    /// Enables or disables the WebView's user zoom (Ctrl+scroll and Ctrl+/-). Effective on the packaged
    /// Windows head's real WebView2. A no-op on the Skia heads, where zoom control is not implemented.
    /// </summary>
    void SetZoomControlEnabled(CoreWebView2 coreWebView2, bool enabled);

    /// <summary>
    /// Begins (or restarts) a whole-page find for the given term, selecting and scrolling to the first match.
    /// Drives the native WKWebView findString on macOS. Inert where ProvidesBuiltInFind is true (the Windows
    /// heads), whose backend supplies its own find bar. Match progress is reported through
    /// FindOptions.OnMatchStateChanged.
    /// </summary>
    Task StartFindAsync(CoreWebView2 coreWebView2, string term, FindOptions options);

    /// <summary>
    /// Advances to the next match of the active find session, wrapping per the session's options. A no-op if
    /// no session was started for this WebView, or where ProvidesBuiltInFind is true.
    /// </summary>
    void FindNext(CoreWebView2 coreWebView2);

    /// <summary>
    /// Steps to the previous match of the active find session, wrapping per the session's options. A no-op if
    /// no session was started for this WebView, or where ProvidesBuiltInFind is true.
    /// </summary>
    void FindPrevious(CoreWebView2 coreWebView2);

    /// <summary>
    /// Ends the active find session, clearing the match selection. Safe to call when no session is active.
    /// </summary>
    void StopFind(CoreWebView2 coreWebView2);
}
