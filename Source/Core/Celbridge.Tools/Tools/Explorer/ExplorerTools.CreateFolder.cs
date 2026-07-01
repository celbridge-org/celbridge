using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Create an empty folder (or open the new-folder dialog for interactive naming).</summary>
    [McpServerTool(Name = "explorer_create_folder")]
    [ToolAlias("explorer.create_folder")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> CreateFolder(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<ICreateResourceDialogCommand>(command =>
            {
                command.ResourceType = ResourceType.Folder;
                command.DestFolderResource = resourceKey;
            });
            if (dialogResult.IsFailure)
            {
                return ToolResponse.Error(dialogResult);
            }

            return ToolResponse.Success("ok");
        }

        var createResult = await ExecuteCommandAsync<ICreateResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestResource = resourceKey;
        });
        if (createResult.IsFailure)
        {
            return ToolResponse.Error(createResult);
        }

        return ToolResponse.Success("ok");
    }
}
