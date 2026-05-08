using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Returns metadata for a single element matched by selector: tag, attributes, computed styles, role/name, rect, child preview.
    /// </summary>
    /// <param name="resource">Resource key of the open document.</param>
    /// <param name="selector">CSS selector. The first match is described.</param>
    /// <param name="childPreviewLimit">Children to include in the preview. Full child count is always reported separately.</param>
    /// <returns>JSON object with tag, selector, role, accessibleName, attributes, visible, rect, computedStyles, and children.</returns>
    [McpServerTool(Name = "webview_inspect")]
    [ToolAlias("webview.inspect")]
    public async partial Task<CallToolResult> Inspect(string resource, string selector, int childPreviewLimit = 5)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolError($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        if (string.IsNullOrEmpty(selector))
        {
            return ToolError("webview_inspect requires a non-empty selector.");
        }

        Logger.LogInformation("webview_inspect resource={Resource} selector={Selector} childPreviewLimit={ChildPreviewLimit}",
            resourceKey, selector, childPreviewLimit);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new InspectOptions(selector, childPreviewLimit);
        var inspectResult = await toolBridge.InspectAsync(resourceKey, options);
        if (inspectResult.IsFailure)
        {
            return ToolError(inspectResult);
        }

        return ToolSuccess(inspectResult.Value);
    }
}
