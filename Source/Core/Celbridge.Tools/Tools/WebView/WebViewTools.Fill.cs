using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Sets the value of an input, textarea, select, or contenteditable element matched
    /// by a CSS selector and dispatches bubbling input and change events. Works for
    /// most framework input bindings (vanilla JS, Lit, Vue, Svelte) because the value
    /// is set through the native HTMLInputElement/HTMLTextAreaElement/HTMLSelectElement
    /// setter so the framework's value-tracking observers fire. Waits up to 5 seconds
    /// for the editor's content-ready signal before dispatching. Requires the
    /// webview-dev-tools feature flag. Works on any open document editor whose package
    /// has not opted out of devtools.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to target.</param>
    /// <param name="selector">CSS selector identifying the element to fill. The first match receives the value and events.</param>
    /// <param name="value">String value to assign. For contenteditable elements this is set as textContent.</param>
    /// <returns>JSON object with `selector`, `tag`, and `value` (the value read back from the element after the assignment).</returns>
    [McpServerTool(Name = "webview_fill")]
    [ToolAlias("webview.fill")]
    public async partial Task<CallToolResult> Fill(string resource, string selector, string value)
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
            return ErrorResult("webview_fill requires a non-empty selector.");
        }

        Logger.LogInformation("webview_fill resource={Resource} selector={Selector} valueLength={ValueLength}",
            resourceKey, selector, value?.Length ?? 0);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new FillOptions(selector, value ?? string.Empty);
        var fillResult = await toolBridge.FillAsync(resourceKey, options);
        if (fillResult.IsFailure)
        {
            return ErrorResult(fillResult.FirstErrorMessage);
        }

        return SuccessResult(fillResult.Value);
    }
}
