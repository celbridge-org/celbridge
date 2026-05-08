using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>
    /// Copies a resource to a new location in the project.
    /// </summary>
    /// <param name="sourceResource">Resource key of the source item.</param>
    /// <param name="destinationResource">Resource key of the destination.</param>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "explorer_copy")]
    [ToolAlias("explorer.copy")]
    public async partial Task<CallToolResult> Copy(string sourceResource, string destinationResource)
    {
        if (!ResourceKey.TryCreate(sourceResource, out var sourceResourceKey))
        {
            return ToolError($"Invalid resource key: '{sourceResource}'");
        }
        if (!ResourceKey.TryCreate(destinationResource, out var destinationResourceKey))
        {
            return ToolError($"Invalid resource key: '{destinationResource}'");
        }

        var copyResult = await ExecuteCommandAsync<ICopyResourceCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.DestResource = destinationResourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
        if (copyResult.IsFailure)
        {
            return ToolError(copyResult);
        }

        return ToolSuccess("ok");
    }
}
