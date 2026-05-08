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
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolError($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        Logger.LogInformation("webview_reload resource={Resource} clearCache={ClearCache}", resourceKey, clearCache);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var reloadResult = await toolBridge.ReloadAsync(resourceKey, clearCache);
        if (reloadResult.IsFailure)
        {
            return ToolError(reloadResult);
        }

        return ToolSuccess("ok");
    }
}
