using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Create an empty file (or open the new-file dialog); use file_write to also write content.</summary>
    [McpServerTool(Name = "explorer_create_file")]
    [ToolAlias("explorer.create_file")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> CreateFile(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<ICreateResourceDialogCommand>(command =>
            {
                command.ResourceType = ResourceType.File;
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
            command.ResourceType = ResourceType.File;
            command.DestResource = resourceKey;
        });
        if (createResult.IsFailure)
        {
            return ToolResponse.Error(createResult);
        }

        return ToolResponse.Success("ok");
    }
}
