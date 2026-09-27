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
    [RelatedGuides("resource_keys", "webview_documents", "webview_devtools")]
    public async partial Task<CallToolResult> GetNetwork(
        string resource,
        int tail = 100,
        bool includeHeaders = false,
        bool includeBodies = false,
        long sinceTimestampMs = 0,
        string frame = "")
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

        Logger.LogInformation("webview_get_network resource={Resource} tail={Tail} includeHeaders={IncludeHeaders} includeBodies={IncludeBodies} since={Since} frame={Frame}",
            resourceKey, tail, includeHeaders, includeBodies, sinceTimestampMs, frame);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        long? since = sinceTimestampMs > 0 ? sinceTimestampMs : null;
        var options = new NetworkQueryOptions(tail, includeHeaders, includeBodies, since, frame);
        var networkResult = await toolBridge.GetNetworkAsync(resourceKey, options);
        if (networkResult.IsFailure)
        {
            return ToolResponse.Error(networkResult);
        }

        return ToolResponse.Success(networkResult.Value);
    }
}
