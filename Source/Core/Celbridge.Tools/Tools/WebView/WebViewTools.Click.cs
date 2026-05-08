using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Dispatch a synthetic click on the first element matching a CSS selector.</summary>
    [McpServerTool(Name = "webview_click")]
    [ToolAlias("webview.click")]
    public async partial Task<CallToolResult> Click(string resource, string selector)
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
            return ToolError("webview_click requires a non-empty selector.");
        }

        Logger.LogInformation("webview_click resource={Resource} selector={Selector}", resourceKey, selector);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new ClickOptions(selector);
        var clickResult = await toolBridge.ClickAsync(resourceKey, options);
        if (clickResult.IsFailure)
        {
            return ToolError(clickResult);
        }

        return ToolSuccess(clickResult.Value);
    }
}
