using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Dispatch a synthetic click on the first element matching a CSS selector.</summary>
    [McpServerTool(Name = "webview_click")]
    [ToolAlias("webview.click")]
    [RelatedGuides("resource_keys", "webview_documents", "webview_devtools")]
    public async partial Task<CallToolResult> Click(string resource, string selector)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.WebViewDevTools);
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (string.IsNullOrEmpty(selector))
        {
            return ToolResponse.Error("webview_click requires a non-empty selector.");
        }

        Logger.LogInformation("webview_click resource={Resource} selector={Selector}", resourceKey, selector);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new ClickOptions(selector);
        var clickResult = await toolBridge.ClickAsync(resourceKey, options);
        if (clickResult.IsFailure)
        {
            return ToolResponse.Error(clickResult);
        }

        return ToolResponse.Success(clickResult.Value);
    }
}
