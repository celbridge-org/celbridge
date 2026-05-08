using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Reloads the WebView; page state is discarded. Use after writing package files. See guides_read(['webview_devtools']).
    /// </summary>
    /// <param name="resource">Resource key of the open document.</param>
    /// <param name="clearCache">When true, clears the WebView HTTP cache so edited sub-resources are refetched. The clear evicts data for every document in the same profile.</param>
    /// <returns>"ok" on success.</returns>
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
