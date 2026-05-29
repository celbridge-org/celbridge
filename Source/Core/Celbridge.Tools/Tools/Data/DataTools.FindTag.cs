using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Find every resource whose .cel sidecar tags list contains the given tag value.</summary>
    [McpServerTool(Name = "data_find_tag", ReadOnly = true)]
    [ToolAlias("data.find_tag")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> FindTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return ToolResponse.Error("tag must be a non-empty string.");
        }

        var commandResult = await ExecuteCommandAsync<IFindTagCommand, IReadOnlyList<ResourceKey>>(command =>
        {
            command.Tag = tag;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var keys = commandResult.Value.Select(m => m.ToString()).ToArray();
        return ToolResponse.Success(SerializeJson(keys));
    }
}
