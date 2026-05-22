using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class MetaDataTools
{
    /// <summary>Find every resource whose frontmatter has the given field matching the given value (scalar equality or list-of-scalar contains).</summary>
    [McpServerTool(Name = "metadata_find", ReadOnly = true)]
    [ToolAlias("metadata.find")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Find(string field, string valueJson)
    {
        await Task.CompletedTask;

        if (string.IsNullOrEmpty(field))
        {
            return ToolResponse.Error("field must be a non-empty string.");
        }

        var parsed = TryParseJsonValue(valueJson);
        if (!parsed.Success)
        {
            return ToolResponse.Error(parsed.Error!);
        }

        // A list-of-scalar value isn't meaningful as a query argument — the
        // index matches values element-wise. Callers pass a single scalar even
        // for list-of-scalar fields.
        if (parsed.Value is System.Collections.IEnumerable
            && parsed.Value is not string)
        {
            return ToolResponse.Error("value_json must be a scalar (string, number, boolean) for find queries; list-of-scalar fields are matched by element.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var metaData = workspaceWrapper.WorkspaceService.ResourceMetaData;
        await metaData.WaitUntilReadyAsync();

        var matches = metaData.FindByMetaData(field, parsed.Value!);
        var keys = matches.Select(m => m.ToString()).ToArray();
        return ToolResponse.Success(SerializeJson(keys));
    }
}
