using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Locates elements by ARIA role+name, visible text, or CSS selector. Exactly one of role, text, or selector must be supplied. See guides_read(['webview_devtools']).
    /// </summary>
    /// <param name="resource">Resource key of the open document.</param>
    /// <param name="role">ARIA role (combines explicit and implicit roles). Empty when not querying by role.</param>
    /// <param name="name">Accessible name substring filter. Ignored unless role is also provided.</param>
    /// <param name="text">Visible text substring (case-insensitive). Empty when not querying by text.</param>
    /// <param name="selector">CSS selector. Empty when not querying by selector.</param>
    /// <param name="maxResults">Maximum matches to return.</param>
    /// <returns>JSON object with mode, totalMatches, returned, and elements (each carrying a selector suitable for webview_inspect).</returns>
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
            return ToolError($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        var modeCount = 0;
        if (!string.IsNullOrEmpty(role)) modeCount++;
        if (!string.IsNullOrEmpty(text)) modeCount++;
        if (!string.IsNullOrEmpty(selector)) modeCount++;
        if (modeCount != 1)
        {
            return ToolError("webview_query requires exactly one of role, text, or selector.");
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
            return ToolError(queryResult);
        }

        return ToolSuccess(queryResult.Value);
    }
}
