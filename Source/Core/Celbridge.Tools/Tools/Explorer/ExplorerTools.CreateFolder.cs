using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Create an empty folder (or open the new-folder dialog for interactive naming).</summary>
    [McpServerTool(Name = "explorer_create_folder")]
    [ToolAlias("explorer.create_folder")]
    public async partial Task<CallToolResult> CreateFolder(string resource, bool showDialog = false)
    {
        const string ToolGuide = "explorer_create_folder";

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<IAddResourceDialogCommand>(command =>
            {
                command.ResourceType = ResourceType.Folder;
                command.DestFolderResource = resourceKey;
            });
            if (dialogResult.IsFailure)
            {
                return ToolResponse.Error(dialogResult, ToolGuide);
            }

            return ToolResponse.Success("ok");
        }

        var addResult = await ExecuteCommandAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestResource = resourceKey;
        });
        if (addResult.IsFailure)
        {
            return ToolResponse.Error(addResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
