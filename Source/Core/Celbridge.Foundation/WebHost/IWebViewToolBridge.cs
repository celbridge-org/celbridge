namespace Celbridge.WebHost;

/// <summary>
/// Options for IWebViewToolBridge.GetConsoleAsync. Tails the most recent
/// entries, suppresses debug-level by default, and optionally filters to entries
/// newer than a given timestamp. Only the entries logged in the frame the call acts
/// on are returned.
/// </summary>
public sealed record ConsoleQueryOptions(
    int Tail = 100,
    bool IncludeDebug = false,
    long? SinceTimestampMs = null,
    string? Frame = null);

/// <summary>
/// Options for IWebViewToolBridge.GetHtmlAsync. A null selector returns the
/// document root. Max-depth bounds traversal so deeply nested trees do not exceed
/// the agent context budget.
/// </summary>
public sealed record GetHtmlOptions(
    string? Selector = null,
    int MaxDepth = 8,
    string? Frame = null);

/// <summary>
/// Discriminator for IWebViewToolBridge.QueryAsync. Exactly one mode is
/// active per call: RoleQuery, TextQuery, or SelectorQuery.
/// </summary>
public abstract record QueryMode;

/// <summary>
/// Locates elements by ARIA role. The role string combines the explicit role
/// attribute and the implicit role for the element's tag (e.g. button, heading).
/// When Name is supplied, role matches are filtered to those whose accessible
/// name contains the substring (case-insensitive).
/// </summary>
public sealed record RoleQuery(string Role, string? Name = null) : QueryMode;

/// <summary>
/// Locates leaf elements whose collapsed visible text contains the substring
/// (case-insensitive).
/// </summary>
public sealed record TextQuery(string Text) : QueryMode;

/// <summary>
/// Locates elements matching a CSS selector, equivalent to document.querySelectorAll.
/// </summary>
public sealed record SelectorQuery(string Selector) : QueryMode;

/// <summary>
/// Options for IWebViewToolBridge.QueryAsync.
/// </summary>
public sealed record QueryOptions(QueryMode Mode, int MaxResults = 20, string? Frame = null);

/// <summary>
/// Options for IWebViewToolBridge.InspectAsync.
/// </summary>
public sealed record InspectOptions(
    string Selector,
    int ChildPreviewLimit = 5,
    string? Frame = null);

/// <summary>
/// Options for IWebViewToolBridge.ClickAsync. Identifies the element to
/// click by CSS selector. Click events are programmatic and dispatched with
/// isTrusted = false, so handlers that gate on isTrusted will not fire.
/// </summary>
public sealed record ClickOptions(string Selector, string? Frame = null);

/// <summary>
/// Options for IWebViewToolBridge.FillAsync. Sets the value of an input,
/// textarea, select, or contenteditable element identified by CSS selector and
/// dispatches input and change events.
/// </summary>
public sealed record FillOptions(string Selector, string Value, string? Frame = null);

/// <summary>
/// Options for IWebViewToolBridge.GetNetworkAsync. Tails the most recent
/// captured fetch and XHR entries. Headers and request/response bodies are
/// opt-in to control context budget. SinceTimestampMs filters to entries newer
/// than a given start time so agents can poll incrementally. Only the requests made
/// by the frame the call acts on are returned.
/// </summary>
public sealed record NetworkQueryOptions(
    int Tail = 100,
    bool IncludeHeaders = false,
    bool IncludeBodies = false,
    long? SinceTimestampMs = null,
    string? Frame = null);

/// <summary>
/// Options for IWebViewToolBridge.ScreenshotAsync. Format selects the
/// image encoding ("jpeg" or "png"). Quality applies to JPEG only (1-100).
/// MaxEdge caps the longer side in pixels (0 disables downscaling). When
/// Selector is provided, the screenshot is clipped to the matched element's
/// bounding rect. Otherwise it shows the frame the call acts on. SettleMs is an
/// additional delay (in milliseconds) the platform applies after the editor's
/// content-ready signal and before the capture, on top of a small fixed paint
/// backstop. Callers bump it when a recent layout-changing operation (such as
/// document_open) may not yet have committed to a stable visual state.
/// </summary>
public sealed record ScreenshotOptions(
    string Format = "jpeg",
    int Quality = 70,
    int MaxEdge = 768,
    string? Selector = null,
    int SettleMs = 0,
    string? Frame = null);

/// <summary>
/// The area of the page a screenshot captures, in CSS pixels. Scale is the multiplier applied to the captured
/// pixels.
/// </summary>
public sealed record ScreenshotClip(double X, double Y, double Width, double Height, double Scale);

/// <summary>
/// A screenshot a web view is asked to capture. A null Clip captures the whole view. SettleMs is a delay in
/// milliseconds before the capture, added to a short fixed delay the view always waits.
/// </summary>
public sealed record ScreenshotRequest(string Format, int Quality, ScreenshotClip? Clip, int SettleMs);

/// <summary>
/// A captured image. Bytes holds the image encoded in Format, JPEG or PNG.
/// </summary>
public sealed record ScreenshotData(string Format, int Width, int Height, byte[] Bytes);

/// <summary>
/// A screenshot taken by IWebViewToolBridge.ScreenshotAsync, with the name of the frame it shows.
/// </summary>
public sealed record WebViewScreenshot(string Frame, ScreenshotData Data);

/// <summary>
/// The result of IWebViewToolBridge.EvalAsync. Value is the expression's value encoded as JSON, and
/// Frame names the frame the expression ran in.
/// </summary>
public sealed record WebViewEvalResult(string Frame, string Value);

/// <summary>
/// Runs the webview_* tools against registered web views. A tool call names a resource, and the bridge finds the
/// view that shows it.
/// A call acts on one frame of the page. The caller names a frame element with a CSS
/// selector, or the page itself as "top". A call that names no frame acts on the frame
/// the page marks as its content frame, or on the page itself when it marks none. A
/// call that acts on a frame waits for the frame to finish loading its page, and its
/// result names the frame.
/// </summary>
public interface IWebViewToolBridge
{
    /// <summary>
    /// Returns the in-page tool shim. A view the tools reach installs it as a document-start script.
    /// </summary>
    string GetShimScript();

    /// <summary>
    /// Registers a web view. Tool calls find it by its current resource until it closes. When two views show
    /// the same resource, the one registered last answers. Registering a view again has no effect.
    /// </summary>
    void Register(IWebView view);

    /// <summary>
    /// Tells the tools the view's content has loaded, so calls waiting for it go ahead. No effect if the view
    /// is not registered.
    /// </summary>
    void NotifyContentReady(IWebView view);

    /// <summary>
    /// Tells the tools the view has started loading new content, so later calls wait for the next
    /// NotifyContentReady. No effect if the view is not registered.
    /// </summary>
    void NotifyContentLoading(IWebView view);

    /// <summary>
    /// Evaluates a JavaScript expression in the frame, in the WebView registered for the
    /// resource. Returns the JSON-encoded value and the name of the frame.
    /// Waits for the editor's content-ready signal (with timeout) before dispatching.
    /// Fails if no WebView is registered for the resource.
    /// </summary>
    Task<Result<WebViewEvalResult>> EvalAsync(ResourceKey resource, string expression, string? frame = null);

    /// <summary>
    /// Reloads the frame in the WebView registered for the resource, discarding its page
    /// state. Console and network buffers are preserved so entries captured before the
    /// reload remain readable. Reloading the page itself clears the HTTP cache first
    /// when clearCache is true. Reloading a frame reloads only that frame's page and
    /// leaves the cache alone. Returns the name of the frame.
    /// </summary>
    Task<Result<string>> ReloadAsync(ResourceKey resource, bool clearCache, string? frame = null);

    /// <summary>
    /// Returns captured console.* messages, uncaught errors, and unhandled promise
    /// rejections from the WebView. The host accumulates entries across reloads so
    /// the buffer survives navigation. Waits for the editor's content-ready signal
    /// (with timeout) before dispatching to the in-page shim.
    /// </summary>
    Task<Result<string>> GetConsoleAsync(ResourceKey resource, ConsoleQueryOptions options);

    /// <summary>
    /// Returns the outerHTML of the document or a subtree. Script and style bodies
    /// are replaced with placeholder markers and whitespace is normalised. Waits
    /// for the editor's content-ready signal (with timeout) before dispatching.
    /// </summary>
    Task<Result<string>> GetHtmlAsync(ResourceKey resource, GetHtmlOptions options);

    /// <summary>
    /// Locates elements by ARIA role + accessible name, visible text, or CSS selector.
    /// Returns stable CSS selectors, bounding rectangles, and visibility flags for each
    /// match. Waits for the editor's content-ready signal (with timeout) before
    /// dispatching.
    /// </summary>
    Task<Result<string>> QueryAsync(ResourceKey resource, QueryOptions options);

    /// <summary>
    /// Locates elements by ARIA role + accessible name, visible text, or CSS selector. Each element carries
    /// its rectangle in the page's viewport in CSS pixels, whether its center is in view, its text and value,
    /// and whether it is checked, disabled or focused. The result also carries the page's devicePixelRatio.
    /// Waits for the editor's content-ready signal (with timeout) before dispatching.
    /// </summary>
    Task<Result<string>> LocateAsync(ResourceKey resource, QueryOptions options);

    /// <summary>
    /// Returns metadata for the element matched by the selector: tag, attributes,
    /// curated computed styles, accessible role and name, bounding rectangle, visibility,
    /// and a child preview. Waits for the editor's content-ready signal (with timeout)
    /// before dispatching.
    /// </summary>
    Task<Result<string>> InspectAsync(ResourceKey resource, InspectOptions options);

    /// <summary>
    /// Dispatches a programmatic mouse-click sequence (mousedown, mouseup, click) on
    /// the element matched by the selector. Events bubble but have isTrusted = false,
    /// so handlers that gate on isTrusted will not fire. Waits for the editor's
    /// content-ready signal (with timeout) before dispatching.
    /// </summary>
    Task<Result<string>> ClickAsync(ResourceKey resource, ClickOptions options);

    /// <summary>
    /// Sets the value of an input, textarea, select, or contenteditable element matched by
    /// the selector and dispatches bubbling input and change events. Waits for the editor's
    /// content-ready signal (with timeout) before dispatching.
    /// </summary>
    Task<Result<string>> FillAsync(ResourceKey resource, FillOptions options);

    /// <summary>
    /// Returns captured fetch and XHR activity from the WebView. The host accumulates
    /// entries across reloads so the buffer survives navigation. Waits for the
    /// editor's content-ready signal (with timeout) before draining the in-page buffer.
    /// </summary>
    Task<Result<string>> GetNetworkAsync(ResourceKey resource, NetworkQueryOptions options);

    /// <summary>
    /// Captures a screenshot of the frame. Supports JPEG and PNG. The JPEG quality
    /// parameter is ignored for PNG. When a selector is supplied, the output is clipped
    /// to the element's bounding rectangle. The longer edge is capped at MaxEdge unless
    /// MaxEdge is non-positive. Fails if the web view is not on screen, or the platform cannot capture it.
    /// </summary>
    Task<Result<WebViewScreenshot>> ScreenshotAsync(ResourceKey resource, ScreenshotOptions options);
}
