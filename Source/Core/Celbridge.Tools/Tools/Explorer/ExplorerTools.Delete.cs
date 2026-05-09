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
        const string ToolGuide = "explorer_delete";

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
                return ToolResponse.Error(dialogResult, ToolGuide);
            }

            return ToolResponse.Success("ok");
        }

        var deleteResult = await ExecuteCommandAsync<IDeleteResourceCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { resourceKey };
        });
        if (deleteResult.IsFailure)
        {
            return ToolResponse.Error(deleteResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
