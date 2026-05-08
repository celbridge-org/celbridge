using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Redoes the most recently undone explorer operation. Does not affect text edits.
    /// </summary>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "explorer_redo")]
    [ToolAlias("explorer.redo")]
    public async partial Task<CallToolResult> Redo()
    {
        var redoResult = await ExecuteCommandAsync<IRedoResourceCommand>();
        if (redoResult.IsFailure)
        {
            return ToolError(redoResult);
        }

        return ToolSuccess("ok");
    }
}
