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
        const string ToolGuide = "explorer_duplicate";

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var duplicateResult = await ExecuteCommandAsync<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        if (duplicateResult.IsFailure)
        {
            return ToolResponse.Error(duplicateResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
