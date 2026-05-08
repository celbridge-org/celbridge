using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Selects a resource in the explorer panel.
    /// </summary>
    /// <param name="resource">Resource key of the item to select.</param>
    /// <param name="showExplorerPanel">Show the explorer panel if hidden.</param>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "explorer_select", ReadOnly = true, Idempotent = true)]
    [ToolAlias("explorer.select")]
    public async partial Task<CallToolResult> Select(string resource, bool showExplorerPanel = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        var selectResult = await ExecuteCommandAsync<ISelectResourceCommand>(command =>
        {
            command.Resource = resourceKey;
            command.ShowExplorerPanel = showExplorerPanel;
        });
        if (selectResult.IsFailure)
        {
            return ToolError(selectResult);
        }

        return ToolSuccess("ok");
    }
}
