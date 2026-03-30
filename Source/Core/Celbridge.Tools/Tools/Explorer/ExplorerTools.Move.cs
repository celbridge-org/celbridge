using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Moves or renames a resource.
    /// </summary>
    /// <param name="sourceResource">Resource key of the source item.</param>
    /// <param name="destinationResource">Resource key of the destination.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "explorer_move")]
    [ToolAlias("explorer.move")]
    public async partial Task<CallToolResult> Move(string sourceResource, string destinationResource)
    {
        if (!ResourceKey.TryCreate(sourceResource, out var sourceResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{sourceResource}'");
        }
        if (!ResourceKey.TryCreate(destinationResource, out var destinationResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{destinationResource}'");
        }

        return await ExecuteCommandAsync<ICopyResourceCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.DestResource = destinationResourceKey;
            command.TransferMode = DataTransferMode.Move;
        });
    }
}
