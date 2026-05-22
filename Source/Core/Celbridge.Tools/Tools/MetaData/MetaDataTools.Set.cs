using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class MetaDataTools
{
    /// <summary>Write a single frontmatter field on a resource's .cel sidecar (creates the sidecar if missing).</summary>
    [McpServerTool(Name = "metadata_set")]
    [ToolAlias("metadata.set")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Set(string resource, string field, string valueJson)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }
        if (string.IsNullOrEmpty(field))
        {
            return ToolResponse.Error("field must be a non-empty string.");
        }

        var parsed = TryParseJsonValue(valueJson);
        if (!parsed.Success)
        {
            return ToolResponse.Error(parsed.Error!);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var metaData = workspaceWrapper.WorkspaceService.ResourceMetaData;
        await metaData.WaitUntilReadyAsync();

        var setResult = await metaData.SetFrontmatterFieldAsync(resourceKey, field, parsed.Value!);
        if (setResult.IsFailure)
        {
            return ToolResponse.Error(setResult);
        }

        // After the sidecar write the registry / metadata indexes catch up via
        // the watcher event. Wait for that drain so the agent's next call sees
        // the new state.
        await metaData.WaitForPendingUpdatesAsync();

        return ToolResponse.Success("ok");
    }
}
