using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Atomically write a batch of fields to a resource's .cel sidecar.</summary>
    [McpServerTool(Name = "data_set_fields", Idempotent = true)]
    [ToolAlias("data.set_fields")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> SetFields(string resource, string fieldsJson)
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

        var parseResult = TryParseFieldsObject(fieldsJson);
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var fields = parseResult.Value;
        if (fields.Count == 0)
        {
            return ToolResponse.Error("fields must contain at least one entry.");
        }

        // Reserved-namespace rejection: any underscore-prefixed name is denied
        // with a typed message that points at the dedicated tool for known
        // reserved fields (e.g. _tags) and a generic message for the rest.
        foreach (var name in fields.Keys)
        {
            if (name.StartsWith("_", StringComparison.Ordinal))
            {
                return ToolResponse.Error(BuildReservedFieldRejection(name));
            }
        }

        var sidecarService = GetRequiredService<IWorkspaceWrapper>().WorkspaceService.ResourceService.Sidecars;
        foreach (var (name, value) in fields)
        {
            if (!sidecarService.IsIndexableValue(value))
            {
                return ToolResponse.Error($"Field '{name}' value is not indexable. Only scalar (string/number/bool) and list-of-scalar values are supported.");
            }
        }

        var commandResult = await ExecuteCommandAsync<ISetFieldsCommand>(command =>
        {
            command.Resource = resourceKey;
            command.Fields = fields;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        return ToolResponse.Success("ok");
    }

    // Parses the fieldsJson argument into a {name -> value} dictionary. The
    // outer JSON object maps name strings to JSON-encoded value literals
    // (matching the singular tool's value_json convention); each per-field
    // failure surfaces with the offending field named.
    private static Result<IReadOnlyDictionary<string, object>> TryParseFieldsObject(string fieldsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson))
        {
            return Result.Fail("fields must be a non-empty JSON object mapping field names to value_json strings.");
        }

        JsonElement element;
        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(fieldsJson);
        }
        catch (JsonException ex)
        {
            return Result.Fail($"fields is not valid JSON: {ex.Message}");
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return Result.Fail("fields must be a JSON object mapping field names to value_json strings.");
        }

        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            if (string.IsNullOrEmpty(name))
            {
                errors.Add("field name is empty");
                continue;
            }
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                errors.Add($"'{name}' value must be a JSON-encoded value string (e.g. \"\\\"Sunset\\\"\", \"42\", \"[\\\"a\\\",\\\"b\\\"]\")");
                continue;
            }
            var valueJson = property.Value.GetString() ?? string.Empty;
            var valueResult = TryParseJsonValue(valueJson);
            if (valueResult.IsFailure)
            {
                errors.Add($"'{name}': {valueResult.FirstErrorMessage}");
                continue;
            }
            fields[name] = valueResult.Value;
        }

        if (errors.Count > 0)
        {
            return Result.Fail($"fields contains {errors.Count} invalid entr{(errors.Count == 1 ? "y" : "ies")}: {string.Join("; ", errors)}");
        }

        return fields;
    }

    private static string BuildReservedFieldRejection(string name)
    {
        if (string.Equals(name, "_tags", StringComparison.Ordinal))
        {
            return "Field '_tags' is in the reserved namespace; use data_add_tags / data_remove_tags / data_list_tags instead of data_set_fields.";
        }
        return $"Field '{name}' is in the reserved namespace (root-level names starting with '_' are reserved for system metadata).";
    }
}
