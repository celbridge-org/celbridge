using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Duplicate a resource in place via the interactive rename dialog (user picks the new name).</summary>
    [McpServerTool(Name = "explorer_duplicate")]
    [ToolAlias("explorer.duplicate")]
    public async partial Task<CallToolResult> Duplicate(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        var duplicateResult = await ExecuteCommandAsync<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        if (duplicateResult.IsFailure)
        {
            return ToolError(duplicateResult);
        }

        return ToolSuccess("ok");
    }
}
