using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Remove a single frontmatter field from a resource's .cel sidecar (no-op if absent).</summary>
    [McpServerTool(Name = "data_remove_field", Destructive = true, Idempotent = true)]
    [ToolAlias("data.remove_field")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> RemoveField(string resource, string field)
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

        var commandResult = await ExecuteCommandAsync<IRemoveFieldCommand>(command =>
        {
            command.Resource = resourceKey;
            command.Field = field;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
