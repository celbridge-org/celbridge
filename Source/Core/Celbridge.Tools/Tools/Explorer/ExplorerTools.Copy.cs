using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Copy a resource to a new destination key (source remains in place).</summary>
    [McpServerTool(Name = "explorer_copy")]
    [ToolAlias("explorer.copy")]
    [RelatedGuides("resource_keys", "undo_semantics")]
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

        var copyResult = await ExecuteCommandAsync<ICopyResourceCommand, CopyCommandResult>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.DestResource = destinationResourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
        if (copyResult.IsFailure)
        {
            return ToolResponse.Error(copyResult);
        }

        var detail = copyResult.Value;

        // Copy doesn't rewrite references, so SkippedReferencers is always empty
        // here. FailedResources still matters: a batch where one resource was
        // refused (destination locked, hidden by the policy, etc.) surfaces the
        // partial outcome with its reason rather than reporting a clean success.
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
