using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Returns captured console.* messages, uncaught errors, and unhandled promise rejections; survives reloads. See guides_read(['webview_devtools']).
    /// </summary>
    /// <param name="resource">Resource key of the open document.</param>
    /// <param name="tail">Maximum recent entries to return.</param>
    /// <param name="includeDebug">When true, include console.debug() entries.</param>
    /// <param name="sinceTimestampMs">When &gt; 0, only entries with timestampMs strictly greater. Use a prior call's timestampMs to poll incrementally.</param>
    /// <returns>JSON object with entries, returned (after filtering), and totalAccumulated.</returns>
    [McpServerTool(Name = "webview_get_console")]
    [ToolAlias("webview.get_console")]
    public async partial Task<CallToolResult> GetConsole(string resource, int tail = 100, bool includeDebug = false, long sinceTimestampMs = 0)
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

        Logger.LogInformation("webview_get_console resource={Resource} tail={Tail} includeDebug={IncludeDebug} since={Since}",
            resourceKey, tail, includeDebug, sinceTimestampMs);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        long? since = sinceTimestampMs > 0 ? sinceTimestampMs : null;
        var options = new ConsoleQueryOptions(tail, includeDebug, since);
        var consoleResult = await toolBridge.GetConsoleAsync(resourceKey, options);
        if (consoleResult.IsFailure)
        {
            return ToolError(consoleResult);
        }

        return ToolSuccess(consoleResult.Value);
    }
}
