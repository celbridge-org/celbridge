using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Returns the outerHTML of the WebView document or a subtree. Script and
    /// style bodies are replaced with `[omitted N bytes]` markers and whitespace
    /// is collapsed so the agent context budget is preserved. Pass a CSS selector
    /// to scope the output to a subtree. Waits up to 5 seconds for the editor's
    /// content-ready signal before dispatching. Requires the webview-dev-tools
    /// feature flag. Works on any open document editor whose package has not
    /// opted out of devtools.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to query.</param>
    /// <param name="selector">Optional CSS selector that scopes the output to a single subtree. Pass an empty string (the default) to return the entire document element.</param>
    /// <param name="maxDepth">Maximum tree depth to include. Children beyond this depth are replaced with a `truncated children` placeholder. Default 8.</param>
    /// <returns>JSON object with `selector` (the scope, null when full document) and `html` (the redacted, depth-bounded outerHTML string).</returns>
    [McpServerTool(Name = "webview_get_html")]
    [ToolAlias("webview.get_html")]
    public async partial Task<CallToolResult> GetHtml(string resource, string selector = "", int maxDepth = 8)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ErrorResult($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        Logger.LogInformation("webview_get_html resource={Resource} selector={Selector} maxDepth={MaxDepth}",
            resourceKey, selector, maxDepth);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var scopedSelector = string.IsNullOrEmpty(selector) ? null : selector;
        var options = new GetHtmlOptions(scopedSelector, maxDepth);
        var htmlResult = await toolBridge.GetHtmlAsync(resourceKey, options);
        if (htmlResult.IsFailure)
        {
            return ErrorResult(htmlResult.FirstErrorMessage);
        }

        return SuccessResult(htmlResult.Value);
    }
}
