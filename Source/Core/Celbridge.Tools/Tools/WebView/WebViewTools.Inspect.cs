using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Returns metadata for a single element in the WebView matched by a CSS
    /// selector: tag, attributes, curated computed styles (display, visibility,
    /// font, color, layout), accessible role and name, bounding rectangle,
    /// visibility flag, and a child preview. Use this after webview_query to
    /// drill into a specific match. Waits up to 5 seconds for the editor's
    /// content-ready signal before dispatching. Requires the webview-dev-tools
    /// feature flag. Works on any open document editor whose package has not
    /// opted out of devtools.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to query.</param>
    /// <param name="selector">CSS selector identifying the element. The first match is described.</param>
    /// <param name="childPreviewLimit">Number of children to include in the preview list. Default 5. The full child count is always reported separately.</param>
    /// <returns>JSON object describing the element: tag, selector, role, accessibleName, attributes, visible, rect, computedStyles, and children (count plus first-N preview).</returns>
    [McpServerTool(Name = "webview_inspect")]
    [ToolAlias("webview.inspect")]
    public async partial Task<CallToolResult> Inspect(string resource, string selector, int childPreviewLimit = 5)
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

        if (string.IsNullOrEmpty(selector))
        {
            return ErrorResult("webview_inspect requires a non-empty selector.");
        }

        Logger.LogInformation("webview_inspect resource={Resource} selector={Selector} childPreviewLimit={ChildPreviewLimit}",
            resourceKey, selector, childPreviewLimit);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new InspectOptions(selector, childPreviewLimit);
        var inspectResult = await toolBridge.InspectAsync(resourceKey, options);
        if (inspectResult.IsFailure)
        {
            return ErrorResult(inspectResult.FirstErrorMessage);
        }

        return SuccessResult(inspectResult.Value);
    }
}
