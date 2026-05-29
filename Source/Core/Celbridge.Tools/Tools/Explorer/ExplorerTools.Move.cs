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

        // The compact "ok" response is reserved for the no-side-effect case: a
        // move that touched no references, had no skipped referencers, and no
        // failed resources. Whenever the move actually cascaded references or
        // produced any structured outcome the agent might want to act on, emit
        // the JSON payload — including the list of referencers that were
        // rewritten so the agent can report what changed without a follow-up
        // grep.
        if (detail.UpdatedReferencers.Count == 0
            && detail.SkippedReferencers.Count == 0
            && detail.FailedResources.Count == 0)
        {
            return ToolResponse.Success("ok");
        }

        string status;
        if (detail.FailedResources.Count > 0)
        {
            status = "partial_failure";
        }
        else if (detail.SkippedReferencers.Count > 0)
        {
            status = "ok_with_skipped_referencers";
        }
        else
        {
            status = "ok";
        }

        var payload = new
        {
            status,
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
