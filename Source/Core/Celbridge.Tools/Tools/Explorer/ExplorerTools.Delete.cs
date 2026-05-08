using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>READ GUIDE FIRST. Remove a resource from the project (file or folder); undoable via explorer_undo.</summary>
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
