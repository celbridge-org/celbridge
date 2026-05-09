using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Copy a resource to a new destination key (source remains in place).</summary>
    [McpServerTool(Name = "explorer_copy")]
    [ToolAlias("explorer.copy")]
    public async partial Task<CallToolResult> Copy(string sourceResource, string destinationResource)
    {
        if (!ResourceKey.TryCreate(sourceResource, out var sourceResourceKey))
        {
            return ToolResponse.InvalidResourceKey(sourceResource);
        }
        if (!ResourceKey.TryCreate(destinationResource, out var destinationResourceKey))
        {
            return ToolResponse.InvalidResourceKey(destinationResource);
        }

        var copyResult = await ExecuteCommandAsync<ICopyResourceCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.DestResource = destinationResourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
        if (copyResult.IsFailure)
        {
            return ToolResponse.Error(copyResult);
        }

        return ToolResponse.Success("ok");
    }
}
