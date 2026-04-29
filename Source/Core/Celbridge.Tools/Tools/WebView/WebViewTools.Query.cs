using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Locates elements in the WebView by ARIA role + accessible name, by visible
    /// text content, or by CSS selector. Exactly one of role, text, or selector
    /// must be supplied. Returns stable CSS selectors generated from each match,
    /// along with bounding rectangles, visibility flags, and accessible names.
    /// Waits up to 5 seconds for the editor's content-ready signal before
    /// dispatching. Requires the webview-dev-tools feature flag. Works on any
    /// open document editor whose package has not opted out of devtools.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to query.</param>
    /// <param name="role">ARIA role to match. Combines explicit role attributes and the implicit role for the element's tag (e.g. button → button, h2 → heading). Pass an empty string when not querying by role.</param>
    /// <param name="name">Accessible name substring used to filter role matches. Compared case-insensitively against aria-label, aria-labelledby, label-for, alt, title, placeholder, value, then text content. Ignored unless `role` is also provided.</param>
    /// <param name="text">Visible text substring. Matches leaf elements whose collapsed text contains this substring (case-insensitive). Pass an empty string when not querying by text.</param>
    /// <param name="selector">CSS selector. Matches the same set as document.querySelectorAll. Pass an empty string when not querying by selector.</param>
    /// <param name="maxResults">Maximum number of matches to return. Default 20.</param>
    /// <returns>JSON object with `mode` (the query mode that ran), `totalMatches`, `returned`, and `elements` (array of `{tag, selector, role, accessibleName, attributes, visible, rect}`). The selector returned for each element is suitable for passing to webview_inspect.</returns>
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
            return ErrorResult($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var modeCount = 0;
        if (!string.IsNullOrEmpty(role)) modeCount++;
        if (!string.IsNullOrEmpty(text)) modeCount++;
        if (!string.IsNullOrEmpty(selector)) modeCount++;
        if (modeCount != 1)
        {
            return ErrorResult("webview_query requires exactly one of role, text, or selector.");
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
            return ErrorResult(queryResult.FirstErrorMessage);
        }

        return SuccessResult(queryResult.Value);
    }
}
