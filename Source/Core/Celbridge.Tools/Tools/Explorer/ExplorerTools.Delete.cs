using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Remove a resource from the project (file or folder); undoable via explorer_undo.</summary>
    [McpServerTool(Name = "explorer_delete", Destructive = true)]
    [ToolAlias("explorer.delete")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> Delete(string resource, bool showDialog = false, string referencePolicy = "require_confirmation")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (!TryParseDeleteReferencePolicy(referencePolicy, out var policy))
        {
            return ToolResponse.Error($"Invalid reference_policy value: '{referencePolicy}'. Valid values: require_confirmation, fail_if_referenced, break_references.");
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
            command.ReferencePolicy = policy;
        });
        if (deleteResult.IsFailure)
        {
            return ToolResponse.Error(deleteResult);
        }

        var detail = deleteResult.Value;

        // The typical case (single resource, deleted cleanly, no external
        // references broken) returns "ok" so the response stays compact. A
        // structured JSON payload is emitted when the agent has actionable
        // information to consume:
        //   - per-resource failures with typed reasons (NotFound, Locked, etc.)
        //   - batch outcomes that aren't a clean success
        //   - a sidecar that didn't cascade alongside its parent
        //   - external references that were touched (under break_references,
        //     these are now dangling; under any policy, the agent may want to
        //     follow up on them).
        if (detail.BatchOutcome == DeleteBatchOutcome.DeletedAll
            && detail.ResourceResults.All(r => r.Outcome == DeleteResourceOutcome.Deleted
                && r.Sidecar != SidecarOutcome.Failed)
            && detail.Referencers.Count == 0)
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
                failureMessage = r.FailureMessage,
            }).ToArray(),
            referencers = detail.Referencers.ToDictionary(
                entry => entry.Key.ToString(),
                entry => entry.Value.Select(r => r.ToString()).ToArray()),
        };

        return ToolResponse.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static bool TryParseDeleteReferencePolicy(string value, out DeleteReferencePolicy policy)
    {
        switch (value)
        {
            case "require_confirmation":
                policy = DeleteReferencePolicy.RequireConfirmation;
                return true;
            case "fail_if_referenced":
                policy = DeleteReferencePolicy.FailIfReferenced;
                return true;
            case "break_references":
                policy = DeleteReferencePolicy.BreakReferences;
                return true;
            default:
                policy = DeleteReferencePolicy.RequireConfirmation;
                return false;
        }
    }
}
