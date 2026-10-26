using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Enumerate the unique tag strings across every healthy sidecar in the workspace.</summary>
    [McpServerTool(Name = "data_list_tags", ReadOnly = true)]
    [ToolAlias("data.list_tags")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> ListTags(bool applySearchExclude = true)
    {
        var commandResult = await ExecuteCommandAsync<IListTagsCommand, IReadOnlyList<string>>(command =>
        {
            command.ApplySearchExclude = applySearchExclude;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var payload = new
        {
            tags = commandResult.Value,
        };
        return ToolResponse.Success(SerializeJson(payload));
    }
}
