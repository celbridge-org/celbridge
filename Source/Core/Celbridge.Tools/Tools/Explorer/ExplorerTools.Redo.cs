using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Redo the last undone explorer-domain operation (file/folder ops only, not text edits).</summary>
    [McpServerTool(Name = "explorer_redo")]
    [ToolAlias("explorer.redo")]
    [RelatedGuides("undo_semantics")]
    public async partial Task<CallToolResult> Redo()
    {
        var redoResult = await ExecuteCommandAsync<IRedoResourceCommand>();
        if (redoResult.IsFailure)
        {
            return ToolResponse.Error(redoResult);
        }

        return ToolResponse.Success("ok");
    }
}
