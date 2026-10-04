using Celbridge.Automation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class UITools
{
    /// <summary>Post a named key press into the app's own event queue, for a key a harness cannot deliver (test automation builds only).</summary>
    [McpServerTool(Name = "ui_press_key", ReadOnly = false, Idempotent = false)]
    [ToolAlias("ui.press_key")]
    [RelatedGuides]
    public async partial Task<CallToolResult> PressKey(string key, string modifiers = "")
    {
#if TEST_AUTOMATION
        if (string.IsNullOrWhiteSpace(key))
        {
            return ToolResponse.Error("Name the key to press, e.g. \"Escape\".");
        }

        // The service delivers the press straight to the UI thread, so it still lands while a modal dialog
        // holds the command queue.
        var inputSimulationService = GetRequiredService<IInputSimulationService>();

        var pressResult = await inputSimulationService.PressKeyAsync(key, modifiers);
        if (pressResult.IsFailure)
        {
            return ToolResponse.Error(pressResult);
        }

        return ToolResponse.Success("ok");
#else
        // The tool stays declared so its guide stays paired with a registered tool, and refuses when called.
        await Task.CompletedTask;
        return ToolResponse.TestAutomationUnavailable("ui_press_key");
#endif
    }
}
