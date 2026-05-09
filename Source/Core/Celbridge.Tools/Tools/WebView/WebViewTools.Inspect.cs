using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Read computed metadata (attributes, styles, role/name, rect, child preview) for one selector match.</summary>
    [McpServerTool(Name = "webview_inspect")]
    [ToolAlias("webview.inspect")]
    public async partial Task<CallToolResult> Inspect(string resource, string selector, int childPreviewLimit = 5)
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
            return ToolResponse.Error("webview_inspect requires a non-empty selector.");
        }

        Logger.LogInformation("webview_inspect resource={Resource} selector={Selector} childPreviewLimit={ChildPreviewLimit}",
            resourceKey, selector, childPreviewLimit);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new InspectOptions(selector, childPreviewLimit);
        var inspectResult = await toolBridge.InspectAsync(resourceKey, options);
        if (inspectResult.IsFailure)
        {
            return ToolResponse.Error(inspectResult);
        }

        return ToolResponse.Success(inspectResult.Value);
    }
}
