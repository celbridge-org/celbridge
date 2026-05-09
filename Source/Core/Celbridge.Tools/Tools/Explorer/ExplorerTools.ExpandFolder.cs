using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Expand or collapse a single folder node in the explorer tree.</summary>
    [McpServerTool(Name = "explorer_expand_folder")]
    [ToolAlias("explorer.expand_folder")]
    public async partial Task<CallToolResult> ExpandFolder(string resource, bool expanded = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var expandResult = await ExecuteCommandAsync<IExpandFolderCommand>(command =>
        {
            command.FolderResource = resourceKey;
            command.Expanded = expanded;
        });
        if (expandResult.IsFailure)
        {
            return ToolResponse.Error(expandResult);
        }

        return ToolResponse.Success("ok");
    }
}
