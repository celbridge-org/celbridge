using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Undoes the most recent file system operation (create, delete, move, rename, copy).
    /// This only affects explorer operations, not document text edits. To undo text edits,
    /// apply a reverse edit using file_apply_edits or file_delete_lines.
    /// </summary>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_undo")]
    [ToolAlias("explorer.undo")]
    public async partial Task<CallToolResult> Undo()
    {
        return await ExecuteCommandAsync<IUndoResourceCommand>();
    }
}
