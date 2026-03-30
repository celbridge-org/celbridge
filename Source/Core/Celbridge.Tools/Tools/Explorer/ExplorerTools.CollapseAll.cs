using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Collapses all expanded folders in the explorer tree.
    /// </summary>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_collapse_all")]
    [ToolAlias("explorer.collapse_all")]
    public async partial Task<CallToolResult> CollapseAll()
    {
        return await ExecuteCommandAsync<ICollapseAllCommand>();
    }
}
