using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class MetaDataTools
{
    /// <summary>Remove a single frontmatter field from a resource's .cel sidecar (no-op if absent).</summary>
    [McpServerTool(Name = "metadata_remove", Destructive = true)]
    [ToolAlias("metadata.remove")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Remove(string resource, string field)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }
        if (string.IsNullOrEmpty(field))
        {
            return ToolResponse.Error("field must be a non-empty string.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var metaData = workspaceWrapper.WorkspaceService.ResourceMetaData;
        await metaData.WaitUntilReadyAsync();

        var removeResult = await metaData.RemoveFrontmatterFieldAsync(resourceKey, field);
        if (removeResult.IsFailure)
        {
            return ToolResponse.Error(removeResult);
        }

        await metaData.WaitForPendingUpdatesAsync();

        return ToolResponse.Success("ok");
    }
}
