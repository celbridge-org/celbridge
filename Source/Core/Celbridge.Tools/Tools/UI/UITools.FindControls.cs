using System.Text.Json;
using Celbridge.Automation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class UITools
{
    /// <summary>Find the app's own controls by automation ID, name or control type, with their frames and state (test automation, Debug builds only).</summary>
    [McpServerTool(Name = "ui_find_controls", ReadOnly = true, Idempotent = true)]
    [ToolAlias("ui.find_controls")]
    [RelatedGuides]
    public async partial Task<CallToolResult> FindControls(string automationId = "", string name = "", string controlType = "")
    {
        var query = new ControlQuery(automationId, name, controlType);
        if (ControlQueryMatcher.IsEmpty(query))
        {
            return ToolResponse.Error(EmptyQueryMessage);
        }

        var automationService = GetRequiredService<IAutomationService>();
        var controlsResult = await automationService.GetControlsAsync();
        if (controlsResult.IsFailure)
        {
            return ToolResponse.Error(controlsResult);
        }
        var snapshot = controlsResult.Value;

        var matching = new List<ControlInfo>();
        foreach (var control in snapshot.Controls)
        {
            if (ControlQueryMatcher.Matches(query, control))
            {
                matching.Add(control);
            }
        }

        var result = snapshot with { Controls = matching };
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
