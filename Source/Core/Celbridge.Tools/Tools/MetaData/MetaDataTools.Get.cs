using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class MetaDataTools
{
    /// <summary>Read a single frontmatter field from a resource's .cel sidecar.</summary>
    [McpServerTool(Name = "metadata_get", ReadOnly = true)]
    [ToolAlias("metadata.get")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Get(string resource, string field)
    {
        await Task.CompletedTask;

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

        var frontmatterResult = metaData.GetFrontmatter(resourceKey);
        if (frontmatterResult.IsFailure)
        {
            return ToolResponse.Error(frontmatterResult);
        }

        if (!frontmatterResult.Value.TryGetValue(field, out var value))
        {
            return ToolResponse.Error($"Field '{field}' is not set on resource '{resource}'.");
        }

        return ToolResponse.Success(SerializeJson(value));
    }
}
