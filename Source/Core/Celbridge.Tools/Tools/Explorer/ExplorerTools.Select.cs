using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Programmatically set the single selected resource in the explorer panel.</summary>
    [McpServerTool(Name = "explorer_select", ReadOnly = true, Idempotent = true)]
    [ToolAlias("explorer.select")]
    public async partial Task<CallToolResult> Select(string resource, bool showExplorerPanel = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var selectResult = await ExecuteCommandAsync<ISelectResourceCommand>(command =>
        {
            command.Resource = resourceKey;
            command.ShowExplorerPanel = showExplorerPanel;
        });
        if (selectResult.IsFailure)
        {
            return ToolResponse.Error(selectResult);
        }

        return ToolResponse.Success("ok");
    }
}
