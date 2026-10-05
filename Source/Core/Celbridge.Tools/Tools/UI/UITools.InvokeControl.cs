using System.Text.Json;
using Celbridge.Automation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by ui_invoke_control: the control that was invoked and the default action it performed, which
/// is Invoke, Toggle, Expand or Select.
/// </summary>
public record class InvokedControlResult(ControlInfo Control, string Action);

public partial class UITools
{
    /// <summary>Perform the default action of one of the app's own controls, as assistive technology does (test automation, Debug builds only).</summary>
    [McpServerTool(Name = "ui_invoke_control", ReadOnly = false, Idempotent = false)]
    [ToolAlias("ui.invoke_control")]
    [RelatedGuides]
    public async partial Task<CallToolResult> InvokeControl(string automationId = "", string name = "", string controlType = "")
    {
        var query = new ControlQuery(automationId, name, controlType);
        if (query.IsEmpty)
        {
            return ToolResponse.Error(EmptyQueryMessage);
        }

        var automationService = GetRequiredService<IAutomationService>();
        var invokeResult = await automationService.InvokeControlAsync(query.Matches);
        if (invokeResult.IsFailure)
        {
            return ToolResponse.Error(invokeResult);
        }
        var invocation = invokeResult.Value;

        var result = new InvokedControlResult(invocation.Control, invocation.Action.ToString());
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
