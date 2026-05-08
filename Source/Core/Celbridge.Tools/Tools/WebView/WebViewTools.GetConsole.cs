using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Read the captured console buffer (console.* messages, errors, unhandled rejections); survives reloads.</summary>
    [McpServerTool(Name = "webview_get_console")]
    [ToolAlias("webview.get_console")]
    public async partial Task<CallToolResult> GetConsole(string resource, int tail = 100, bool includeDebug = false, long sinceTimestampMs = 0)
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

        Logger.LogInformation("webview_get_console resource={Resource} tail={Tail} includeDebug={IncludeDebug} since={Since}",
            resourceKey, tail, includeDebug, sinceTimestampMs);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        long? since = sinceTimestampMs > 0 ? sinceTimestampMs : null;
        var options = new ConsoleQueryOptions(tail, includeDebug, since);
        var consoleResult = await toolBridge.GetConsoleAsync(resourceKey, options);
        if (consoleResult.IsFailure)
        {
            return ToolResponse.Error(consoleResult);
        }

        return ToolResponse.Success(consoleResult.Value);
    }
}
