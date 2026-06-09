using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for resource .cel sidecar data: per-resource field read /
/// write, tag affordances, and project-wide consistency checks.
/// </summary>
[McpServerToolType]
public partial class DataTools : AgentToolBase
{
    public DataTools(IApplicationServiceProvider services) : base(services) { }

    private static string SerializeJson(object? value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Parses a JSON string-array argument into a List of strings. Fails when
    /// the input is not a JSON array, or any element is non-string. The label
    /// names the offending parameter for the error message.
    /// </summary>
    private static Result<List<string>> TryParseStringArray(string json, string label)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Fail($"{label} must be a non-empty JSON array of strings.");
        }

        JsonElement element;
        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException ex)
        {
            return Result.Fail($"{label} is not valid JSON: {ex.Message}");
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return Result.Fail($"{label} must be a JSON array of strings.");
        }

        var list = new List<string>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return Result.Fail($"{label}[{index}] is not a string.");
            }
            var value = item.GetString();
            if (value is null)
            {
                return Result.Fail($"{label}[{index}] is null.");
            }
            list.Add(value);
            index++;
        }
        return list;
    }

    /// <summary>
    /// Parses a JSON value string into a CLR object the data layer can accept
    /// (scalar or list-of-scalar). Fails for unsupported shapes
    /// (nested objects, mixed-type arrays).
    /// </summary>
    private static Result<object> TryParseJsonValue(string valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return Result.Fail("value_json must be a non-empty JSON-encoded value.");
        }

        JsonElement element;
        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(valueJson);
        }
        catch (JsonException ex)
        {
            return Result.Fail($"value_json is not valid JSON: {ex.Message}");
        }

        var converted = ConvertJsonElement(element);
        if (converted is null)
        {
            return Result.Fail("value_json must be a scalar (string, number, boolean) or list of scalars; nested objects are not supported.");
        }

        return converted;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    return l;
                }
                if (element.TryGetDouble(out var d))
                {
                    return d;
                }
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    var converted = ConvertJsonElement(item);
                    if (converted is null)
                    {
                        return null;
                    }
                    list.Add(converted);
                }
                return list;
            default:
                return null;
        }
    }

    /// <summary>
    /// Returns an error response when the resource key names a .cel sidecar
    /// file rather than its parent. The .cel extension is reserved for
    /// metadata sidecars; the data tools always address the parent resource
    /// and resolve to the sibling sidecar internally. Returns null when the
    /// key is a valid parent-shaped resource.
    /// </summary>
    private CallToolResult? ValidateNotSidecarKey(ResourceKey resource, string original)
    {
        var sidecarService = GetRequiredService<IWorkspaceWrapper>().WorkspaceService.ResourceService.Sidecars;
        if (sidecarService.IsSidecarKey(resource))
        {
            return ToolResponse.Error(
                $"Resource '{original}' targets a .cel metadata sidecar; the data tools address the parent resource instead. "
                + $"Pass the same key without the trailing .cel.");
        }
        return null;
    }
}
