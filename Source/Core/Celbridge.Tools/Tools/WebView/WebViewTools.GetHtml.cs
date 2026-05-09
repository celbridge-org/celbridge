using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Read the raw outerHTML of the WebView document or a subtree (script/style bodies redacted).</summary>
    [McpServerTool(Name = "webview_get_html")]
    [ToolAlias("webview.get_html")]
    public async partial Task<CallToolResult> GetHtml(string resource, string selector = "", int maxDepth = 8)
    {
        const string ToolGuide = "webview_get_html";

        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.WebViewDevTools, "webview");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        // Clamp maxDepth so a callsite passing int.MaxValue cannot trigger an
        // unbounded recursion through the live DOM in the shim's pruneDepth.
        const int MaxAllowedDepth = 50;
        var clampedDepth = maxDepth < 0 ? 0 : Math.Min(maxDepth, MaxAllowedDepth);

        Logger.LogInformation("webview_get_html resource={Resource} selector={Selector} maxDepth={MaxDepth}",
            resourceKey, selector, clampedDepth);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var scopedSelector = string.IsNullOrEmpty(selector) ? null : selector;
        var options = new GetHtmlOptions(scopedSelector, clampedDepth);
        var htmlResult = await toolBridge.GetHtmlAsync(resourceKey, options);
        if (htmlResult.IsFailure)
        {
            return ToolResponse.Error(htmlResult, ToolGuide);
        }

        return ToolResponse.Success(htmlResult.Value);
    }
}
