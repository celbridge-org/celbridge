using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Duplicates a resource. Always shows an interactive dialog where the user can choose the new name.
    /// </summary>
    /// <param name="resource">Resource key of the item to duplicate.</param>
    /// <returns>"ok" on success.</returns>
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
