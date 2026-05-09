using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Redo the last undone explorer-domain operation (file/folder ops only, not text edits).</summary>
    [McpServerTool(Name = "explorer_redo")]
    [ToolAlias("explorer.redo")]
    public async partial Task<CallToolResult> Redo()
    {
        const string ToolGuide = "explorer_redo";

        var redoResult = await ExecuteCommandAsync<IRedoResourceCommand>();
        if (redoResult.IsFailure)
        {
            return ToolResponse.Error(redoResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
