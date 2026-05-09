using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Move a resource to a new key (also used for non-interactive rename).</summary>
    [McpServerTool(Name = "explorer_move")]
    [ToolAlias("explorer.move")]
    public async partial Task<CallToolResult> Move(string sourceResource, string destinationResource)
    {
        const string ToolGuide = "explorer_move";

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
            command.TransferMode = DataTransferMode.Move;
        });
        if (copyResult.IsFailure)
        {
            return ToolResponse.Error(copyResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
