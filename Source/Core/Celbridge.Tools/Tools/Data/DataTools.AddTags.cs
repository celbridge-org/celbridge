using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Atomically append a batch of tags to a resource's tag list.</summary>
    [McpServerTool(Name = "data_add_tags", Idempotent = true)]
    [ToolAlias("data.add_tags")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> AddTags(string resource, string tagsJson)
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

        var parseResult = TryParseStringArray(tagsJson, "tags");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var tags = parseResult.Value;
        if (tags.Count == 0)
        {
            return ToolResponse.Error("tags must contain at least one entry.");
        }
        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return ToolResponse.Error("tags must not contain empty strings.");
            }
        }

        var commandResult = await ExecuteCommandAsync<IAddTagsCommand>(command =>
        {
            command.Resource = resourceKey;
            command.Tags = tags;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
