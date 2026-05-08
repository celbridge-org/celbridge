using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Undo the last explorer-domain operation (create/delete/move/rename/copy, not text edits).</summary>
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
