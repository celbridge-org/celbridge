using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Creates an empty file. Use file_write to create and write content in one step; this tool is for empty files or the interactive dialog.
    /// </summary>
    /// <param name="resource">Resource key for the new file, or the parent folder when using the dialog.</param>
    /// <param name="showDialog">If true, show the create file dialog for interactive naming.</param>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "explorer_create_file")]
    [ToolAlias("explorer.create_file")]
    public async partial Task<CallToolResult> CreateFile(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<IAddResourceDialogCommand>(command =>
            {
                command.ResourceType = ResourceType.File;
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
            command.ResourceType = ResourceType.File;
            command.DestResource = resourceKey;
        });
        if (addResult.IsFailure)
        {
            return ToolError(addResult);
        }

        return ToolSuccess("ok");
    }
}
