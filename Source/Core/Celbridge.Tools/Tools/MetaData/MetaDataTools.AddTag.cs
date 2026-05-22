using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class MetaDataTools
{
    /// <summary>Append a tag to a resource's tags list (creates the sidecar if missing; idempotent).</summary>
    [McpServerTool(Name = "metadata_add_tag")]
    [ToolAlias("metadata.add_tag")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> AddTag(string resource, string tag)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }
        if (string.IsNullOrEmpty(tag))
        {
            return ToolResponse.Error("tag must be a non-empty string.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var metaData = workspaceWrapper.WorkspaceService.ResourceMetaData;
        await metaData.WaitUntilReadyAsync();

        var addResult = await metaData.AddTagAsync(resourceKey, tag);
        if (addResult.IsFailure)
        {
            return ToolResponse.Error(addResult);
        }

        await metaData.WaitForPendingUpdatesAsync();

        return ToolResponse.Success("ok");
    }
}
