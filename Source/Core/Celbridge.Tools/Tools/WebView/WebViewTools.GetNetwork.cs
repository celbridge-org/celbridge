using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Returns captured fetch and XMLHttpRequest activity from the WebView. The host
    /// accumulates entries across reloads, so requests issued before a reload remain
    /// readable after it. Default returns the most recent 100 entries with headers
    /// and bodies omitted to control context budget. Opt in via includeHeaders or
    /// includeBodies. Response bodies are captured up to ~16KB with truncation
    /// markers. Binary or non-text responses are recorded as a placeholder.
    /// Waits up to 5 seconds for the editor's content-ready signal before
    /// dispatching. Requires the webview-dev-tools feature flag. Works on any open
    /// document editor whose package has not opted out of devtools.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to query.</param>
    /// <param name="tail">Maximum number of recent entries to return. Default 100.</param>
    /// <param name="includeHeaders">When true, include captured request and response headers per entry. Default false.</param>
    /// <param name="includeBodies">When true, include the request body description and response body (truncated to ~16KB) per entry. Default false because bodies dominate context.</param>
    /// <param name="sinceTimestampMs">When greater than 0, only include entries whose startTimeMs is strictly greater than this value. Pass the most recent startTimeMs from a previous call to poll incrementally. Pass 0 (the default) to disable timestamp filtering.</param>
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
            return ErrorResult($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        Logger.LogInformation("webview_get_network resource={Resource} tail={Tail} includeHeaders={IncludeHeaders} includeBodies={IncludeBodies} since={Since}",
            resourceKey, tail, includeHeaders, includeBodies, sinceTimestampMs);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        long? since = sinceTimestampMs > 0 ? sinceTimestampMs : null;
        var options = new NetworkQueryOptions(tail, includeHeaders, includeBodies, since);
        var networkResult = await toolBridge.GetNetworkAsync(resourceKey, options);
        if (networkResult.IsFailure)
        {
            return ErrorResult(networkResult.FirstErrorMessage);
        }

        return SuccessResult(networkResult.Value);
    }
}
