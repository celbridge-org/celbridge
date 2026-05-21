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

        // For the typical case (clean rename with no skipped referencers and no
        // failed resources), return the simple "ok" so the response stays compact.
        // Surface a structured JSON payload only when there is actionable
        // information for the agent: skipped referencers (the cascade left a
        // stale reference because the file was read-only or locked) or failed
        // resources (the move itself didn't apply for a resource in the batch).
        if (detail.SkippedReferencers.Count == 0
            && detail.FailedResources.Count == 0)
        {
            return ToolResponse.Success("ok");
        }

        var payload = new
        {
            status = detail.FailedResources.Count == 0 ? "ok_with_skipped_referencers" : "partial_failure",
            updatedReferencers = detail.UpdatedReferencers.Select(r => r.ToString()).ToArray(),
            skippedReferencers = detail.SkippedReferencers.Select(s => new
            {
                resource = s.Resource.ToString(),
                reason = s.Reason.ToString(),
                message = s.Message,
            }).ToArray(),
            failedResources = detail.FailedResources.Select(r => r.ToString()).ToArray(),
        };

        return ToolResponse.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
