using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Read a single frontmatter field from a resource's .cel sidecar.</summary>
    [McpServerTool(Name = "data_get_field", ReadOnly = true)]
    [ToolAlias("data.get_field")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> GetField(string resource, string field)
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

        var commandResult = await ExecuteCommandAsync<IGetFieldCommand, object>(command =>
        {
            command.Resource = resourceKey;
            command.Field = field;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success(SerializeJson(commandResult.Value));
    }
}
