using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for resource .cel sidecar data: per-resource frontmatter
/// read / write, tag affordances, named content blocks, and project-wide
/// consistency checks.
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
