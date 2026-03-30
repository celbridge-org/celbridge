using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Expands or collapses a folder in the explorer tree.
    /// </summary>
    /// <param name="resource">Resource key of the folder.</param>
    /// <param name="expanded">If true, expand the folder. If false, collapse it.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_expand_folder")]
    [ToolAlias("explorer.expand_folder")]
    public async partial Task<CallToolResult> ExpandFolder(string resource, bool expanded = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        return await ExecuteCommandAsync<IExpandFolderCommand>(command =>
        {
            command.FolderResource = resourceKey;
            command.Expanded = expanded;
        });
    }
}
