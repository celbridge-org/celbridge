using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Duplicates a resource. Always shows an interactive dialog where the user can choose the new name.
    /// </summary>
    /// <param name="resource">Resource key of the item to duplicate.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_duplicate")]
    [ToolAlias("explorer.duplicate")]
    public async partial Task<CallToolResult> Duplicate(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        return await ExecuteCommandAsync<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
