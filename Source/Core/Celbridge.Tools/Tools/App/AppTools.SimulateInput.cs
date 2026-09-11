using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Perform a test-automation input operation, for input an external harness cannot deliver (debug builds only).</summary>
    [McpServerTool(Name = "app_simulate_input", ReadOnly = false, Idempotent = false)]
    [ToolAlias("app.simulate_input")]
    [RelatedGuides]
    public async partial Task<CallToolResult> SimulateInput(string operation, string key = "", string modifiers = "")
    {
#if DEBUG
        if (!string.Equals(operation, "key", StringComparison.Ordinal))
        {
            return ToolResponse.Error($"Unknown operation '{operation}'. Supported operations: key.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return ToolResponse.Error("The key operation requires a key name, e.g. \"Escape\".");
        }

        // The service delivers the press straight to the UI thread, so it still lands while a modal dialog
        // holds the command queue — which is when a caller most needs it.
        var inputSimulationService = GetRequiredService<IInputSimulationService>();

        var pressResult = await inputSimulationService.PressKeyAsync(key, modifiers);
        if (pressResult.IsFailure)
        {
            return ToolResponse.Error(pressResult);
        }

        return ToolResponse.Success("ok");
#else
        // Test automation is a debug-build facility. The tool stays declared so its guide stays paired
        // with a registered tool, and refuses when called.
        await Task.CompletedTask;
        return ToolResponse.Error("app_simulate_input is available in debug builds only.");
#endif
    }
}
