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
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "explorer_delete", Destructive = true)]
    [ToolAlias("explorer.delete")]
    public async partial Task<CallToolResult> Delete(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<IDeleteResourceDialogCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resourceKey };
            });
            if (dialogResult.IsFailure)
            {
                return ToolError(dialogResult);
            }

            return ToolSuccess("ok");
        }

        var deleteResult = await ExecuteCommandAsync<IDeleteResourceCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { resourceKey };
        });
        if (deleteResult.IsFailure)
        {
            return ToolError(deleteResult);
        }

        return ToolSuccess("ok");
    }
}
