using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Find elements by ARIA role+name, visible text, or CSS selector (exactly one mode per call).</summary>
    [McpServerTool(Name = "webview_query")]
    [ToolAlias("webview.query")]
    public async partial Task<CallToolResult> Query(
        string resource,
        string role = "",
        string name = "",
        string text = "",
        string selector = "",
        int maxResults = 20)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.WebViewDevTools);
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var modeCount = 0;
        if (!string.IsNullOrEmpty(role)) modeCount++;
        if (!string.IsNullOrEmpty(text)) modeCount++;
        if (!string.IsNullOrEmpty(selector)) modeCount++;
        if (modeCount != 1)
        {
            return ToolResponse.Error("webview_query requires exactly one of role, text, or selector.");
        }

        Logger.LogInformation("webview_query resource={Resource} role={Role} name={Name} text={Text} selector={Selector} maxResults={MaxResults}",
            resourceKey, role, name, text, selector, maxResults);

        QueryMode mode;
        if (!string.IsNullOrEmpty(role))
        {
            string? scopedName = string.IsNullOrEmpty(name) ? null : name;
            mode = new RoleQuery(role, scopedName);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            mode = new TextQuery(text);
        }
        else
        {
            mode = new SelectorQuery(selector);
        }

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var options = new QueryOptions(mode, maxResults);
        var queryResult = await toolBridge.QueryAsync(resourceKey, options);
        if (queryResult.IsFailure)
        {
            return ToolResponse.Error(queryResult);
        }

        var queryJson = queryResult.Value;
        return ToolResponse.Success(queryJson);
    }
}
