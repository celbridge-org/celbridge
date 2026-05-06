using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Shows the rename dialog for a resource. Renaming is always interactive.
    /// </summary>
    /// <param name="resource">Resource key of the item to rename.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_rename")]
    [ToolAlias("explorer.rename")]
    public async partial Task<CallToolResult> Rename(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        var renameResult = await ExecuteCommandAsync<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        if (renameResult.IsFailure)
        {
            return ToolError(renameResult);
        }

        return ToolSuccess("ok");
    }
}
