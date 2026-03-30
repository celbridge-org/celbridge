using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Redoes the most recently undone file system operation.
    /// This only affects explorer operations, not document text edits.
    /// </summary>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_redo")]
    [ToolAlias("explorer.redo")]
    public async partial Task<CallToolResult> Redo()
    {
        return await ExecuteCommandAsync<IRedoResourceCommand>();
    }
}
