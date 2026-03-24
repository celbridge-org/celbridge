using Celbridge.Explorer;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for interacting with the explorer panel.
/// </summary>
[McpServerToolType]
public partial class ExplorerTools : AgentToolBase
{
    public ExplorerTools(IApplicationServiceProvider services) : base(services) {}

    /// <summary>
    /// Selects a resource in the explorer panel.
    /// </summary>
    /// <param name="resource">Resource key of the item to select.</param>
    /// <param name="showExplorerPanel">Show the explorer panel if hidden.</param>
    [McpServerTool(Name = "explorer_select", ReadOnly = true, Idempotent = true)]
    [ToolAlias("resource.select")]
    public async partial Task<CallToolResult> Select(string resource, bool showExplorerPanel = true)
    {
        return await ExecuteCommandAsync<ISelectResourceCommand>(command =>
        {
            command.Resource = resource;
            command.ShowExplorerPanel = showExplorerPanel;
        });
    }
}
