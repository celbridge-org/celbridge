using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Dispatches a programmatic mouse-click sequence (mousedown, mouseup, click) on
    /// the element matched by a CSS selector. Events bubble but have isTrusted = false,
    /// so handlers that gate on event.isTrusted will not fire. If a click appears to do
    /// nothing, use webview_eval to verify the listener is registered before assuming
    /// the click failed. Waits up to 5 seconds for the editor's content-ready signal
    /// before dispatching. Requires the webview-dev-tools feature flag. Works on any
    /// open document editor whose package has not opted out of devtools.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to target.</param>
    /// <param name="selector">CSS selector identifying the element to click. The first match receives the click sequence.</param>
    /// <returns>JSON object with `selector`, `tag`, `visible`, `rect`, and `isTrusted` (always false because the events are programmatic).</returns>
    [McpServerTool(Name = "webview_click")]
    [ToolAlias("webview.click")]
    public async partial Task<CallToolResult> Click(string resource, string selector)
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

        if (string.IsNullOrEmpty(selector))
        {
            return ErrorResult("webview_click requires a non-empty selector.");
        }

        Logger.LogInformation("webview_click resource={Resource} selector={Selector}", resourceKey, selector);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new ClickOptions(selector);
        var clickResult = await toolBridge.ClickAsync(resourceKey, options);
        if (clickResult.IsFailure)
        {
            return ErrorResult(clickResult.FirstErrorMessage);
        }

        return SuccessResult(clickResult.Value);
    }
}
