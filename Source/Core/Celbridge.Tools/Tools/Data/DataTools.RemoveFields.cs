using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Atomically remove a batch of fields from a resource's .cel sidecar.</summary>
    [McpServerTool(Name = "data_remove_fields", Destructive = true, Idempotent = true)]
    [ToolAlias("data.remove_fields")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> RemoveFields(string resource, string namesJson)
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

        // Reserved-namespace names are silent no-ops at this surface — the
        // field tools don't address the reserved underscore-prefixed keys.
        var filtered = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (name.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }
            filtered.Add(name);
        }
        if (filtered.Count == 0)
        {
            return ToolResponse.Success("ok");
        }

        var commandResult = await ExecuteCommandAsync<IRemoveFieldsCommand>(command =>
        {
            command.Resource = resourceKey;
            command.Names = filtered;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }
}
