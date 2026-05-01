using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Returns captured console.* messages, uncaught errors, and unhandled promise
    /// rejections from the WebView. The host accumulates entries across reloads,
    /// so errors logged before a reload remain readable after it. Entries are
    /// returned as JSON. The default tail is the most recent 100 entries with
    /// debug-level messages suppressed. Pass tail or includeDebug to widen.
    /// Waits up to 5 seconds for the editor's content-ready signal before
    /// dispatching. Requires the webview-dev-tools feature flag. Works on any
    /// open document editor whose package has not opted out of devtools.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to query.</param>
    /// <param name="tail">Maximum number of recent entries to return. Default 100. Pass a larger value to widen the window.</param>
    /// <param name="includeDebug">When true, include console.debug() entries. Default false because debug output is typically noise.</param>
    /// <param name="sinceTimestampMs">When greater than 0, only include entries with timestampMs strictly greater than this value. Use the timestampMs returned in a previous call to poll incrementally. Pass 0 (the default) to disable timestamp filtering.</param>
    /// <returns>JSON object with `entries` (array of `{level, timestampMs, args, stack}`), `returned` (count after filtering), and `totalAccumulated` (total entries the host has ever captured for this resource).</returns>
    [McpServerTool(Name = "webview_get_console")]
    [ToolAlias("webview.get_console")]
    public async partial Task<CallToolResult> GetConsole(string resource, int tail = 100, bool includeDebug = false, long sinceTimestampMs = 0)
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

        Logger.LogInformation("webview_get_console resource={Resource} tail={Tail} includeDebug={IncludeDebug} since={Since}",
            resourceKey, tail, includeDebug, sinceTimestampMs);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        long? since = sinceTimestampMs > 0 ? sinceTimestampMs : null;
        var options = new ConsoleQueryOptions(tail, includeDebug, since);
        var consoleResult = await toolBridge.GetConsoleAsync(resourceKey, options);
        if (consoleResult.IsFailure)
        {
            return ErrorResult(consoleResult.FirstErrorMessage);
        }

        return SuccessResult(consoleResult.Value);
    }
}
