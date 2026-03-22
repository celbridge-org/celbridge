using Celbridge.Commands;
using Celbridge.Explorer;
using ModelContextProtocol.Server;

namespace Celbridge.MCPTools.Tools;

/// <summary>
/// MCP tools for interacting with the explorer panel.
/// </summary>
[McpServerToolType]
public class ExplorerTools
{
    private readonly ICommandService _commandService;

    public ExplorerTools(ICommandService commandService)
    {
        _commandService = commandService;
    }

    /// <summary>
    /// Selects a resource in the explorer panel.
    /// </summary>
    /// <param name="resource">Resource key of the item to select.</param>
    /// <param name="showExplorerPanel">Show the explorer panel if hidden.</param>
    [McpServerTool(Name = "explorer_select", ReadOnly = true, Idempotent = true)]
    [ToolAlias("select")]
    public void Select(string resource, bool showExplorerPanel = true)
    {
        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = resource;
            command.ShowExplorerPanel = showExplorerPanel;
        });
    }
}
