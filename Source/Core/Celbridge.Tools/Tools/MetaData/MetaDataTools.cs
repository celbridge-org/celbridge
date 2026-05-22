using System.Text.Json;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for resource sidecar metadata: per-resource frontmatter read /
/// write, tag affordances, and project-wide search by indexed field.
/// </summary>
[McpServerToolType]
public partial class MetaDataTools : AgentToolBase
{
    public MetaDataTools(IApplicationServiceProvider services) : base(services) { }

    private static string SerializeJson(object? value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Parses a JSON value string into a CLR object the metadata service can
    /// accept (scalar or list-of-scalar). Returns null for unsupported shapes
    /// (nested objects, mixed-type arrays). Used by the Set and Find tools so
    /// callers can pass typed values through a single string parameter.
    /// </summary>
    private static (bool Success, object? Value, string? Error) TryParseJsonValue(string valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return (false, null, "value_json must be a non-empty JSON-encoded value.");
        }

        JsonElement element;
        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(valueJson);
        }
        catch (JsonException ex)
        {
            return (false, null, $"value_json is not valid JSON: {ex.Message}");
        }

        var converted = ConvertJsonElement(element);
        if (converted is null)
        {
            return (false, null, "value_json must be a scalar (string, number, boolean) or list of scalars; nested objects are not supported.");
        }

        return (true, converted, null);
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
}
