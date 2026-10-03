using System.Text.Json;
using Celbridge.UserInterface;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Find the app's own controls by automation ID, name or control type, with their frames and state (test automation, debug builds only).</summary>
    [McpServerTool(Name = "app_find_controls", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.find_controls")]
    [RelatedGuides]
    public async partial Task<CallToolResult> FindControls(string automationId = "", string name = "", string controlType = "")
    {
#if DEBUG
        var lookupService = GetRequiredService<IControlLookupService>();
        var query = new ControlQuery(automationId, name, controlType);

        var lookupResult = await lookupService.FindControlsAsync(query);
        if (lookupResult.IsFailure)
        {
            return ToolResponse.Error(lookupResult);
        }
        var result = lookupResult.Value;

        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
#else
        // Test automation is a debug-build facility. The tool stays declared so its guide stays paired
        // with a registered tool, and refuses when called.
        await Task.CompletedTask;
        return ToolResponse.Error("app_find_controls is available in debug builds only.");
#endif
    }
}
