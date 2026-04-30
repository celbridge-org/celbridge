namespace Celbridge.WebHost;

/// <summary>
/// Options for IDocumentWebViewToolBridge.GetConsoleAsync. Tails the most recent
/// entries, suppresses debug-level by default, and optionally filters to entries
/// newer than a given timestamp.
/// </summary>
public sealed record ConsoleQueryOptions(
    int Tail = 100,
    bool IncludeDebug = false,
    long? SinceTimestampMs = null);

/// <summary>
/// Options for IDocumentWebViewToolBridge.GetHtmlAsync. A null selector returns the
/// document root. Max-depth bounds traversal so deeply nested trees do not exceed
/// the agent context budget.
/// </summary>
public sealed record GetHtmlOptions(
    string? Selector = null,
    int MaxDepth = 8);

/// <summary>
/// Discriminator for IDocumentWebViewToolBridge.QueryAsync. Exactly one mode is
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
/// Options for IDocumentWebViewToolBridge.QueryAsync.
/// </summary>
public sealed record QueryOptions(QueryMode Mode, int MaxResults = 20);

/// <summary>
/// Options for IDocumentWebViewToolBridge.InspectAsync.
/// </summary>
public sealed record InspectOptions(
    string Selector,
    int ChildPreviewLimit = 5);

/// <summary>
/// Options for IDocumentWebViewToolBridge.ClickAsync. Identifies the element to
/// click by CSS selector. Click events are programmatic and dispatched with
/// isTrusted = false, so handlers that gate on isTrusted will not fire.
/// </summary>
public sealed record ClickOptions(string Selector);

/// <summary>
/// Options for IDocumentWebViewToolBridge.FillAsync. Sets the value of an input,
/// textarea, select, or contenteditable element identified by CSS selector and
/// dispatches input and change events.
/// </summary>
public sealed record FillOptions(string Selector, string Value);

/// <summary>
/// Options for IDocumentWebViewToolBridge.GetNetworkAsync. Tails the most recent
/// captured fetch and XHR entries. Headers and request/response bodies are
/// opt-in to control context budget. SinceTimestampMs filters to entries newer
/// than a given start time so agents can poll incrementally.
/// </summary>
public sealed record NetworkQueryOptions(
    int Tail = 100,
    bool IncludeHeaders = false,
    bool IncludeBodies = false,
    long? SinceTimestampMs = null);

/// <summary>
/// Options for IDocumentWebViewToolBridge.ScreenshotAsync. Format selects the
/// image encoding ("jpeg" or "png"). Quality applies to JPEG only (1-100).
/// MaxEdge caps the longer side in pixels (0 disables downscaling). When
/// Selector is provided, the screenshot is clipped to the matched element's
/// bounding rect. SettleMs is an additional delay (in milliseconds) the
/// platform applies after the editor's content-ready signal and before the
/// capture, on top of a small fixed paint backstop. Callers bump it when a
/// recent layout-changing operation (such as document_open) may not yet
/// have committed to a stable visual state.
/// </summary>
public sealed record ScreenshotOptions(
    string Format = "jpeg",
    int Quality = 70,
    int MaxEdge = 768,
    string? Selector = null,
    int SettleMs = 0);

/// <summary>
/// Clip rectangle and scale factor passed to a platform screenshot delegate.
/// Coordinates are in CSS pixels. Scale is the multiplier the platform applies
/// to the captured pixels.
/// </summary>
public sealed record ScreenshotClip(double X, double Y, double Width, double Height, double Scale);

/// <summary>
/// Platform-neutral request passed from the bridge to the per-WebView screenshot
/// delegate. The delegate encodes the captured image to base64 and returns it.
/// SettleMs is the caller-supplied additional delay in milliseconds applied
/// before the platform capture, on top of a small fixed paint backstop the
/// platform delegate applies unconditionally.
/// </summary>
public sealed record ScreenshotRequest(string Format, int Quality, ScreenshotClip? Clip, int SettleMs);

/// <summary>
/// Result returned from a platform screenshot delegate and from IDocumentWebViewToolBridge.ScreenshotAsync.
/// Bytes is the encoded image in the requested format (already a JPEG/PNG byte stream). Callers
/// write it to disk or hand it to a multimodal sink without further decoding.
/// </summary>
public sealed record ScreenshotData(string Format, int Width, int Height, byte[] Bytes);

/// <summary>
/// Host-side registry and execution gateway for the webview_* MCP tool namespace.
/// Bridges those tools to the in-page WebView surface. Not related to WebView2's
/// built-in browser DevTools. Document views register themselves when their WebView
/// is ready and unregister on teardown.
/// </summary>
public interface IDocumentWebViewToolBridge
{
    /// <summary>
    /// Returns the JavaScript source of the in-page tool bridge shim, suitable for
    /// injection via AddScriptToExecuteOnDocumentCreatedAsync. The content is cached
    /// after the first read.
    /// </summary>
    string GetShimScript();

    /// <summary>
    /// Registers an eligible WebView with the tool bridge. Called by document views
    /// (contribution editors and HTML viewers) once their WebView is initialized.
    /// The screenshot delegate is optional. Pass null on platforms or surfaces that
    /// do not support a native snapshot API.
    /// </summary>
    void Register(
        ResourceKey resource,
        Func<string, Task<string>> evalAsync,
        Func<bool, Task> reloadAsync,
        Func<ScreenshotRequest, Task<ScreenshotData>>? screenshotAsync = null);

    /// <summary>
    /// Removes a previously registered WebView. Called when the document view tears down.
    /// </summary>
    void Unregister(ResourceKey resource);

    /// <summary>
    /// Notifies the bridge that the editor's content has finished loading and gated
    /// tool calls (eval, inspection) may dispatch. Document views call this on the
    /// editor's readiness signal — notifyContentLoaded for contribution editors and
    /// NavigationCompleted for the HTML viewer. Idempotent. Safe to call repeatedly
    /// and in any order relative to NotifyContentLoading. No effect if the resource
    /// is not registered.
    /// </summary>
    void NotifyContentReady(ResourceKey resource);

    /// <summary>
    /// Notifies the bridge that the editor has begun loading new content. Subsequent
    /// gated tool calls block until the next NotifyContentReady (or time out).
    /// Document views call this on navigation/reload start. Idempotent. Safe to call
    /// repeatedly and in any order relative to NotifyContentReady. No effect if the
    /// resource is not registered.
    /// </summary>
    void NotifyContentLoading(ResourceKey resource);

    /// <summary>
    /// Evaluates a JavaScript expression in the WebView registered for the resource.
    /// Returns the JSON-encoded result produced by the WebView's eval primitive.
    /// Waits for the editor's content-ready signal (with timeout) before dispatching.
    /// Fails if no WebView is registered for the resource.
    /// </summary>
    Task<Result<string>> EvalAsync(ResourceKey resource, string expression);

    /// <summary>
    /// Reloads the WebView registered for the resource. Page state is discarded;
    /// console and network buffers are preserved so entries captured before the
    /// reload remain readable. When clearCache is true, the HTTP cache is cleared
    /// before reloading.
    /// </summary>
    Task<Result> ReloadAsync(ResourceKey resource, bool clearCache);

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
    /// Captures a screenshot of the WebView. Supports JPEG and PNG. The JPEG quality
    /// parameter is ignored for PNG. When a selector is supplied, the output is clipped
    /// to the element's bounding rectangle. The longer edge is capped at MaxEdge unless
    /// MaxEdge is non-positive. Fails when the registered WebView did not provide a
    /// screenshot delegate (e.g. on a platform without a native snapshot API).
    /// </summary>
    Task<Result<ScreenshotData>> ScreenshotAsync(ResourceKey resource, ScreenshotOptions options);
}
