using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Creates an empty file in the project. Pass show_dialog=true for interactive mode where the user can choose the name and location.
    /// Tip: document_write can create and write files in a single step, so you only need this tool
    /// when you want an empty file or want the interactive dialog.
    /// </summary>
    /// <param name="resource">Resource key for the new file, or the parent folder when using the dialog.</param>
    /// <param name="showDialog">If true, show the create file dialog for interactive naming.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_create_file")]
    [ToolAlias("explorer.create_file")]
    public async partial Task<CallToolResult> CreateFile(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (showDialog)
        {
            return await ExecuteCommandAsync<IAddResourceDialogCommand>(command =>
            {
                command.ResourceType = ResourceType.File;
                command.DestFolderResource = resourceKey;
            });
        }

        return await ExecuteCommandAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestResource = resourceKey;
        });
    }
}
