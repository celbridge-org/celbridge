using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class MetaDataTools
{
    /// <summary>List the full frontmatter for a resource's .cel sidecar as a JSON object.</summary>
    [McpServerTool(Name = "metadata_list", ReadOnly = true)]
    [ToolAlias("metadata.list")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> List(string resource)
    {
        await Task.CompletedTask;

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var metaData = workspaceWrapper.WorkspaceService.ResourceMetaData;
        await metaData.WaitUntilReadyAsync();

        var frontmatterResult = metaData.GetFrontmatter(resourceKey);
        if (frontmatterResult.IsFailure)
        {
            // No frontmatter is documented as an empty object, not an error,
            // so callers can iterate uniformly.
            return ToolResponse.Success("{}");
        }

        return ToolResponse.Success(SerializeJson(frontmatterResult.Value));
    }
}
