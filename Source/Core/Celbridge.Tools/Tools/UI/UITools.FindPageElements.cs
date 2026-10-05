using System.Text.Json;
using Celbridge.Automation;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class UITools
{
    /// <summary>Find elements of a document's page by CSS selector, ARIA role or text, with their frames in the window and state (test automation, Debug builds only).</summary>
    [McpServerTool(Name = "ui_find_page_elements", ReadOnly = true, Idempotent = true)]
    [ToolAlias("ui.find_page_elements")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> FindPageElements(
        string resource,
        string selector = "",
        string role = "",
        string name = "",
        string text = "",
        string frame = "",
        int maxResults = 20)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var modeCount = 0;
        if (!string.IsNullOrEmpty(selector)) modeCount++;
        if (!string.IsNullOrEmpty(role)) modeCount++;
        if (!string.IsNullOrEmpty(text)) modeCount++;
        if (modeCount != 1)
        {
            return ToolResponse.Error("ui_find_page_elements requires exactly one of selector, role or text.");
        }

        QueryMode mode;
        if (!string.IsNullOrEmpty(selector))
        {
            mode = new SelectorQuery(selector);
        }
        else if (!string.IsNullOrEmpty(role))
        {
            string? scopedName = string.IsNullOrEmpty(name) ? null : name;
            mode = new RoleQuery(role, scopedName);
        }
        else
        {
            mode = new TextQuery(text);
        }

        var automationService = GetRequiredService<IAutomationService>();
        var options = new QueryOptions(mode, maxResults, frame);
        var findResult = await automationService.FindPageElementsAsync(resourceKey, options);
        if (findResult.IsFailure)
        {
            return ToolResponse.Error(findResult);
        }
        var snapshot = findResult.Value;

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        return ToolResponse.Success(json);
    }
}
