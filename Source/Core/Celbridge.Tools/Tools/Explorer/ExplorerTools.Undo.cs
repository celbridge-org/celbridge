using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Undoes the most recent explorer operation (create, delete, move, rename, copy). Does not affect text edits.
    /// </summary>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "explorer_undo")]
    [ToolAlias("explorer.undo")]
    public async partial Task<CallToolResult> Undo()
    {
        var undoResult = await ExecuteCommandAsync<IUndoResourceCommand>();
        if (undoResult.IsFailure)
        {
            return ToolError(undoResult);
        }

        return ToolSuccess("ok");
    }
}
