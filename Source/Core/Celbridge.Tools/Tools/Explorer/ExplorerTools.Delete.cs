using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Remove a resource from the project (file or folder); undoable via explorer_undo.</summary>
    [McpServerTool(Name = "explorer_delete", Destructive = true)]
    [ToolAlias("explorer.delete")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> Delete(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<IDeleteResourceDialogCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resourceKey };
            });
            if (dialogResult.IsFailure)
            {
                return ToolResponse.Error(dialogResult);
            }

            return ToolResponse.Success("ok");
        }

        var deleteResult = await ExecuteCommandAsync<IDeleteResourceCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { resourceKey };
        });
        if (deleteResult.IsFailure)
        {
            return ToolResponse.Error(deleteResult);
        }

        return ToolResponse.Success("ok");
    }
}
