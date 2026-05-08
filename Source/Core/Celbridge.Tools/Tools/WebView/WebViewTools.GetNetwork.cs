using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Read the captured network log (fetch/XHR activity); survives reloads.</summary>
    [McpServerTool(Name = "webview_get_network")]
    [ToolAlias("webview.get_network")]
    public async partial Task<CallToolResult> GetNetwork(
        string resource,
        int tail = 100,
        bool includeHeaders = false,
        bool includeBodies = false,
        long sinceTimestampMs = 0)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolResponse.Error($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.Error($"Invalid resource key: '{resource}'");
        }

        Logger.LogInformation("webview_get_network resource={Resource} tail={Tail} includeHeaders={IncludeHeaders} includeBodies={IncludeBodies} since={Since}",
            resourceKey, tail, includeHeaders, includeBodies, sinceTimestampMs);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        long? since = sinceTimestampMs > 0 ? sinceTimestampMs : null;
        var options = new NetworkQueryOptions(tail, includeHeaders, includeBodies, since);
        var networkResult = await toolBridge.GetNetworkAsync(resourceKey, options);
        if (networkResult.IsFailure)
        {
            return ToolResponse.Error(networkResult);
        }

        return ToolResponse.Success(networkResult.Value);
    }
}
