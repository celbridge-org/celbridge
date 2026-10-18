using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Move a resource to a new key (also used for non-interactive rename).</summary>
    [McpServerTool(Name = "explorer_move")]
    [ToolAlias("explorer.move")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> Move(string sourceResource, string destinationResource)
    {
        if (!ResourceKey.TryCreate(sourceResource, out var sourceResourceKey))
        {
            return ToolResponse.InvalidResourceKey(sourceResource);
        }
        if (!ResourceKey.TryCreate(destinationResource, out var destinationResourceKey))
        {
            return ToolResponse.InvalidResourceKey(destinationResource);
        }

        var moveResult = await ExecuteCommandAsync<ICopyResourceCommand, CopyCommandResult>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.DestResource = destinationResourceKey;
            command.TransferMode = DataTransferMode.Move;
        });
        if (moveResult.IsFailure)
        {
            return ToolResponse.Error(moveResult);
        }

        var detail = moveResult.Value;

        // A refused resource produces a partial_failure payload with its reason.
        if (detail.FailedResources.Count == 0)
        {
            return ToolResponse.Success("ok");
        }

        var payload = new
        {
            status = "partial_failure",
            failedResources = detail.FailedResources.Select(r => new
            {
                resource = r.Resource.ToString(),
                message = r.Message,
            }).ToArray(),
        };

        return ToolResponse.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
