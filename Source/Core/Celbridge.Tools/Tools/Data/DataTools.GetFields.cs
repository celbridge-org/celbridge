using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Read a batch of fields from a resource's .cel sidecar in one call.</summary>
    [McpServerTool(Name = "data_get_fields", ReadOnly = true)]
    [ToolAlias("data.get_fields")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> GetFields(string resource, string namesJson)
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

        var parseResult = TryParseStringArray(namesJson, "names");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var names = parseResult.Value;
        if (names.Count == 0)
        {
            return ToolResponse.Error("names must contain at least one entry.");
        }

        var commandResult = await ExecuteCommandAsync<IGetFieldsCommand, IReadOnlyList<GetFieldResult>>(command =>
        {
            command.Resource = resourceKey;
            command.Names = names;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var payload = new List<object>(commandResult.Value.Count);
        foreach (var result in commandResult.Value)
        {
            if (result.Found)
            {
                payload.Add(new { name = result.Name, found = true, value = result.Value });
            }
            else
            {
                payload.Add(new { name = result.Name, found = false });
            }
        }
        return ToolResponse.Success(SerializeJson(payload));
    }
}
