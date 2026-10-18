using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Remove a file or folder. The result says whether explorer_undo can restore it.</summary>
    [McpServerTool(Name = "explorer_delete", Destructive = true, Idempotent = true)]
    [ToolAlias("explorer.delete")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> Delete(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<IDeleteResourceDialogCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resourceKey };
            });
            if (dialogResult.IsFailure)
            {
                return ToolResponse.Error(dialogResult);
            }

            return ToolResponse.Success("ok");
        }

        var deleteResult = await ExecuteCommandAsync<IDeleteResourceCommand, DeleteCommandResult>(command =>
        {
            command.Resources = new List<ResourceKey> { resourceKey };
        });
        if (deleteResult.IsFailure)
        {
            return ToolResponse.Error(deleteResult);
        }

        var detail = deleteResult.Value;

        // The typical case (single resource, deleted cleanly) returns "ok" so
        // the response stays compact. A structured JSON payload is emitted when
        // the agent has actionable information to consume:
        //   - per-resource failures with typed reasons (NotFound, Locked, etc.)
        //   - batch outcomes that aren't a clean success
        //   - a sidecar that didn't cascade alongside its parent
        //   - a delete that explorer_undo cannot reverse
        if (detail.BatchOutcome == DeleteBatchOutcome.DeletedAll
            && detail.ResourceResults.All(r => r.Outcome == DeleteResourceOutcome.Deleted
                && r.Sidecar != SidecarOutcome.Failed
                && r.Undoable))
        {
            return ToolResponse.Success("ok");
        }

        var payload = new
        {
            batchOutcome = detail.BatchOutcome.ToString(),
            resourceResults = detail.ResourceResults.Select(r => new
            {
                resource = r.Resource.ToString(),
                outcome = r.Outcome.ToString(),
                sidecar = r.Sidecar.ToString(),
                undoable = r.Undoable,
                failureMessage = r.FailureMessage,
            }).ToArray(),
        };

        return ToolResponse.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
