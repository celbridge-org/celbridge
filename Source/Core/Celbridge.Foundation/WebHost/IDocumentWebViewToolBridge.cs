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
/// document root; max-depth bounds traversal so deeply nested trees do not exceed
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
/// Host-side registry and execution gateway for the webview_* MCP tool namespace.
/// Bridges those tools to the in-page WebView surface; not related to WebView2's
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
    /// </summary>
    void Register(ResourceKey resource, Func<string, Task<string>> evalAsync, Func<bool, Task> reloadAsync);

    /// <summary>
    /// Removes a previously registered WebView. Called when the document view tears down.
    /// </summary>
    void Unregister(ResourceKey resource);

    /// <summary>
    /// Notifies the bridge that the editor's content has finished loading and gated
    /// tool calls (eval, inspection) may dispatch. Document views call this on the
    /// editor's readiness signal — notifyContentLoaded for contribution editors and
    /// NavigationCompleted for the HTML viewer. Idempotent; safe to call repeatedly
    /// and in any order relative to NotifyContentLoading. No effect if the resource
    /// is not registered.
    /// </summary>
    void NotifyContentReady(ResourceKey resource);

    /// <summary>
    /// Notifies the bridge that the editor has begun loading new content. Subsequent
    /// gated tool calls block until the next NotifyContentReady (or time out).
    /// Document views call this on navigation/reload start. Idempotent; safe to call
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
    /// Reloads the WebView registered for the resource. Page state is discarded by design.
    /// When clearCache is true, the WebView's HTTP cache (in-memory and on-disk) is
    /// cleared before reloading. The accumulated console buffer is preserved across
    /// the reload so errors logged before the reload remain readable through
    /// GetConsoleAsync. Fails if no WebView is registered for the resource.
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
}
