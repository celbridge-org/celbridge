using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Write a single frontmatter field on a resource's .cel sidecar (creates the sidecar if missing).</summary>
    [McpServerTool(Name = "data_set_field", Idempotent = true)]
    [ToolAlias("data.set_field")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> SetField(string resource, string field, string valueJson)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }
        var sidecarError = ValidateNotSidecarKey(resourceKey, resource);
        if (sidecarError is not null)
        {
            return sidecarError;
        }
        if (string.IsNullOrEmpty(field))
        {
            return ToolResponse.Error("field must be a non-empty string.");
        }

        var parseResult = TryParseJsonValue(valueJson);
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var parsedValue = parseResult.Value;

        var sidecarService = GetRequiredService<IWorkspaceWrapper>().WorkspaceService.ResourceService.SidecarService;
        if (!sidecarService.IsIndexableValue(parsedValue))
        {
            return ToolResponse.Error($"Field '{field}' value is not indexable. Only scalar (string/number/bool) and list-of-scalar values are supported.");
        }

        var commandResult = await ExecuteCommandAsync<ISetFieldCommand>(command =>
        {
            command.Resource = resourceKey;
            command.Field = field;
            command.Value = parsedValue;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
