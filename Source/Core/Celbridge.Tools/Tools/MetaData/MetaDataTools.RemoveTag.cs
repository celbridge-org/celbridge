using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class MetaDataTools
{
    /// <summary>Remove a tag from a resource's tags list (no-op if absent; idempotent).</summary>
    [McpServerTool(Name = "metadata_remove_tag")]
    [ToolAlias("metadata.remove_tag")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> RemoveTag(string resource, string tag)
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

        var removeResult = await metaData.RemoveTagAsync(resourceKey, tag);
        if (removeResult.IsFailure)
        {
            return ToolResponse.Error(removeResult);
        }

        await metaData.WaitForPendingUpdatesAsync();

        return ToolResponse.Success("ok");
    }
}
