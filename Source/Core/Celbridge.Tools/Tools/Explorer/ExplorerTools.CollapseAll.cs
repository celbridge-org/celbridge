using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Collapse every expanded folder in the explorer tree.</summary>
    [McpServerTool(Name = "explorer_collapse_all")]
    [ToolAlias("explorer.collapse_all")]
    public async partial Task<CallToolResult> CollapseAll()
    {
        const string ToolGuide = "explorer_collapse_all";

        var collapseResult = await ExecuteCommandAsync<ICollapseAllCommand>();
        if (collapseResult.IsFailure)
        {
            return ToolResponse.Error(collapseResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
