using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Set the value of an input, textarea, select, or contenteditable element and fire input/change events.</summary>
    [McpServerTool(Name = "webview_fill")]
    [ToolAlias("webview.fill")]
    public async partial Task<CallToolResult> Fill(string resource, string selector, string value)
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

        if (string.IsNullOrEmpty(selector))
        {
            return ToolError("webview_fill requires a non-empty selector.");
        }

        Logger.LogInformation("webview_fill resource={Resource} selector={Selector} valueLength={ValueLength}",
            resourceKey, selector, value.Length);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new FillOptions(selector, value);
        var fillResult = await toolBridge.FillAsync(resourceKey, options);
        if (fillResult.IsFailure)
        {
            return ToolError(fillResult);
        }

        return ToolSuccess(fillResult.Value);
    }
}
