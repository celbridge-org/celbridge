using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Celbridge.Tools;

/// <summary>
/// Deserialises MCP tool argument JSON into agent-shaped Result failures.
/// On JsonException, unmapped-property errors list the valid names on T
/// (or its element type for collections) and other errors strip Celbridge.*
/// type prefixes from the message. Called from AgentToolBase.ParseJsonArgument.
/// </summary>
internal static class JsonArgumentParser
{
    public static Result<T> Parse<T>(string json, string label, JsonSerializerOptions options) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            return Result<T>.Fail($"Invalid {label}: argument is required.");
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, options);
            if (value is null)
            {
                return Result<T>.Fail($"Invalid {label}: must be a non-null value.");
            }
            return value;
        }
        catch (JsonException ex)
        {
            return Result<T>.Fail($"Invalid {label}: {FormatError<T>(ex, options)}");
        }
    }

    private static readonly Regex QuotedNameRegex = new("'([^']+)'", RegexOptions.Compiled);

    private static readonly Regex InternalNamespaceRegex = new(@"Celbridge(?:\.\w+)+\.(\w+)", RegexOptions.Compiled);

    // Unmapped-property errors get a friendly "unknown property X. Valid
    // properties: ..." rewrite; everything else returns the JsonException
    // message with any Celbridge.* type prefixes stripped.
    private static string FormatError<T>(JsonException ex, JsonSerializerOptions options)
    {
        var message = ex.Message ?? string.Empty;

        if (message.Contains("could not be mapped to any .NET member"))
        {
            var badProperty = ExtractFirstQuoted(message);
            var validNames = GetJsonPropertyNames(GetCollectionElementType(typeof(T)), options);

            if (!string.IsNullOrEmpty(badProperty) && validNames.Count > 0)
            {
                return $"unknown property '{badProperty}'. Valid properties: {string.Join(", ", validNames)}.";
            }
        }

        return InternalNamespaceRegex.Replace(message, "$1");
    }

    private static string? ExtractFirstQuoted(string message)
    {
        var match = QuotedNameRegex.Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    // Unwraps arrays and the common collection interfaces to their element type.
    private static Type GetCollectionElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType() ?? type;
        }

        if (type.IsGenericType)
        {
            var generic = type.GetGenericTypeDefinition();
            if (generic == typeof(List<>)
                || generic == typeof(IReadOnlyList<>)
                || generic == typeof(IList<>)
                || generic == typeof(IEnumerable<>)
                || generic == typeof(IReadOnlyCollection<>)
                || generic == typeof(ICollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return type;
    }

    // JSON property names on a type, honouring [JsonPropertyName] and the
    // supplied naming policy. Empty for primitives, strings, and types with
    // no public instance properties.
    private static IReadOnlyList<string> GetJsonPropertyNames(Type type, JsonSerializerOptions options)
    {
        if (type == typeof(string) || type.IsPrimitive)
        {
            return Array.Empty<string>();
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var names = new List<string>(properties.Length);
        foreach (var prop in properties)
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attr is not null)
            {
                names.Add(attr.Name);
            }
            else
            {
                var jsonName = options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
                names.Add(jsonName);
            }
        }
        return names;
    }
}
