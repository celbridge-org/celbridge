using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Append a tag to a resource's tags list (creates the sidecar if missing; idempotent).</summary>
    [McpServerTool(Name = "data_add_tag", Idempotent = true)]
    [ToolAlias("data.add_tag")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> AddTag(string resource, string tag)
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

        var commandResult = await ExecuteCommandAsync<IAddTagCommand>(command =>
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
