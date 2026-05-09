using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Reload the WebView page; clears the HTTP cache by default so edited sub-resources are refetched.</summary>
    [McpServerTool(Name = "webview_reload")]
    [ToolAlias("webview.reload")]
    public async partial Task<CallToolResult> Reload(string resource, bool clearCache = true)
    {
        const string ToolGuide = "webview_reload";

        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.WebViewDevTools, "webview");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        Logger.LogInformation("webview_reload resource={Resource} clearCache={ClearCache}", resourceKey, clearCache);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var reloadResult = await toolBridge.ReloadAsync(resourceKey, clearCache);
        if (reloadResult.IsFailure)
        {
            return ToolResponse.Error(reloadResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
