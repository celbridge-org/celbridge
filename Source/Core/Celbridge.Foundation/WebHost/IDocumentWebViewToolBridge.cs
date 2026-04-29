namespace Celbridge.WebHost;

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
    /// Evaluates a JavaScript expression in the WebView registered for the resource.
    /// Returns the JSON-encoded result produced by the WebView's eval primitive.
    /// Fails if no WebView is registered for the resource.
    /// </summary>
    Task<Result<string>> EvalAsync(ResourceKey resource, string expression);

    /// <summary>
    /// Reloads the WebView registered for the resource. Page state is discarded by design.
    /// When clearCache is true, the WebView's HTTP cache (in-memory and on-disk) is
    /// cleared before reloading. Fails if no WebView is registered for the resource.
    /// </summary>
    Task<Result> ReloadAsync(ResourceKey resource, bool clearCache);
}
