using System.Text.Json;
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
        const string ToolGuide = "webview_query";

        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.WebViewDevTools, "webview");
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
            return ToolResponse.Error("webview_query requires exactly one of role, text, or selector.", ToolGuide);
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
            return ToolResponse.Error(queryResult, ToolGuide);
        }

        var queryJson = queryResult.Value;
        if (HasZeroMatches(queryJson))
        {
            // Zero matches is a documented stuck-point: speculative reselectors
            // are usually the wrong response. Route the agent to the per-tool
            // guide, which lists the recurring causes (selector miss, not yet
            // mounted, content-ready not signalled, canvas-painted UI).
            return ToolResponse.SuccessWithGuide(
                queryJson,
                new GuideReference(ToolGuide, "zero matches — see selector, timing, and editor-binding notes"));
        }

        return ToolResponse.Success(queryJson);
    }

    private static bool HasZeroMatches(string queryJson)
    {
        try
        {
            using var document = JsonDocument.Parse(queryJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("totalMatches", out var totalMatchesElement))
            {
                return false;
            }

            return totalMatchesElement.ValueKind == JsonValueKind.Number
                && totalMatchesElement.GetInt32() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
