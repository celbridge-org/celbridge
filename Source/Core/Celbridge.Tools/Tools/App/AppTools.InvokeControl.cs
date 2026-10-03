using System.Text.Json;
using Celbridge.UserInterface;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by app_invoke_control: the control that was invoked and the default action it performed, which
/// is Invoke, Toggle, Expand or Select.
/// </summary>
public record class InvokedControlResult(ControlInfo Control, string Action);

public partial class AppTools
{
    /// <summary>Perform the default action of one of the app's own controls, as assistive technology does (test automation, debug builds only).</summary>
    [McpServerTool(Name = "app_invoke_control", ReadOnly = false, Idempotent = false)]
    [ToolAlias("app.invoke_control")]
    [RelatedGuides]
    public async partial Task<CallToolResult> InvokeControl(string automationId = "", string name = "", string controlType = "")
    {
#if DEBUG
        var lookupService = GetRequiredService<IControlLookupService>();
        var query = new ControlQuery(automationId, name, controlType);

        var invokeResult = await lookupService.InvokeControlAsync(query);
        if (invokeResult.IsFailure)
        {
            return ToolResponse.Error(invokeResult);
        }
        var invocation = invokeResult.Value;

        var result = new InvokedControlResult(invocation.Control, invocation.Action.ToString());
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
#else
        // Test automation is a debug-build facility. The tool stays declared so its guide stays paired
        // with a registered tool, and refuses when called.
        await Task.CompletedTask;
        return ToolResponse.Error("app_invoke_control is available in debug builds only.");
#endif
    }
}
