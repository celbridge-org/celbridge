using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Deletes a resource from the project. Pass show_dialog=true for a confirmation dialog.
    /// </summary>
    /// <param name="resource">Resource key of the item to delete.</param>
    /// <param name="showDialog">If true, show a delete confirmation dialog.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_delete", Destructive = true)]
    [ToolAlias("explorer.delete")]
    public async partial Task<CallToolResult> Delete(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (showDialog)
        {
            return await ExecuteCommandAsync<IDeleteResourceDialogCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resourceKey };
            });
        }

        return await ExecuteCommandAsync<IDeleteResourceCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { resourceKey };
        });
    }
}
