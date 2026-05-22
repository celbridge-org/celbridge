using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Remove a tag from a resource's tags list (no-op if absent; idempotent).</summary>
    [McpServerTool(Name = "data_remove_tag", Idempotent = true)]
    [ToolAlias("data.remove_tag")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> RemoveTag(string resource, string tag)
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
        if (string.IsNullOrEmpty(tag))
        {
            return ToolResponse.Error("tag must be a non-empty string.");
        }

        var commandResult = await ExecuteCommandAsync<IRemoveTagCommand>(command =>
        {
            command.Resource = resourceKey;
            command.Tag = tag;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
