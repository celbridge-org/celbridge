using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Returns captured fetch/XHR activity; survives reloads. Headers and bodies are opt-in to control payload. See guides_read(['webview_devtools']).
    /// </summary>
    /// <param name="resource">Resource key of the open document.</param>
    /// <param name="tail">Maximum recent entries to return.</param>
    /// <param name="includeHeaders">When true, include request and response headers.</param>
    /// <param name="includeBodies">When true, include request body description and response body (truncated to ~16KB).</param>
    /// <param name="sinceTimestampMs">When &gt; 0, only entries with startTimeMs strictly greater. Use a prior call's startTimeMs to poll incrementally.</param>
    /// <returns>JSON object with `entries` (array of `{id, type, method, url, status, startTimeMs, durationMs, requestSize, responseSize, requestHeaders?, responseHeaders?, requestBodyDescription?, responseBody?, error?}`), `returned` (count after filtering), and `totalAccumulated` (total entries the host has captured for this resource).</returns>
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
            return ToolError($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        Logger.LogInformation("webview_get_network resource={Resource} tail={Tail} includeHeaders={IncludeHeaders} includeBodies={IncludeBodies} since={Since}",
            resourceKey, tail, includeHeaders, includeBodies, sinceTimestampMs);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        long? since = sinceTimestampMs > 0 ? sinceTimestampMs : null;
        var options = new NetworkQueryOptions(tail, includeHeaders, includeBodies, since);
        var networkResult = await toolBridge.GetNetworkAsync(resourceKey, options);
        if (networkResult.IsFailure)
        {
            return ToolError(networkResult);
        }

        return ToolSuccess(networkResult.Value);
    }
}
