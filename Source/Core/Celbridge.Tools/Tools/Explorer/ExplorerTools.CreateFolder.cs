using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Creates an empty folder in the project. Pass show_dialog=true for interactive mode where the user can choose the name and location.
    /// </summary>
    /// <param name="resource">Resource key for the new folder, or the parent folder when using the dialog.</param>
    /// <param name="showDialog">If true, show the create folder dialog for interactive naming.</param>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "explorer_create_folder")]
    [ToolAlias("explorer.create_folder")]
    public async partial Task<CallToolResult> CreateFolder(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
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
                return ToolError(dialogResult);
            }

            return ToolSuccess("ok");
        }

        var addResult = await ExecuteCommandAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestResource = resourceKey;
        });
        if (addResult.IsFailure)
        {
            return ToolError(addResult);
        }

        return ToolSuccess("ok");
    }
}
