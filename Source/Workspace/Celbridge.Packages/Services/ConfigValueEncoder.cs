using System.Globalization;
using System.Text.Json;

namespace Celbridge.Packages;

/// <summary>
/// Type-checks raw TOML values against a config descriptor and encodes them in the normalized
/// string form used by the editor Options channel. String lists are encoded as JSON arrays.
/// </summary>
public static class ConfigValueEncoder
{
    /// <summary>
    /// Validates a raw TOML value (string, bool, long, double, or IReadOnlyList of string)
    /// against the descriptor's type and returns the normalized string encoding.
    /// </summary>
    public static Result<string> Encode(object? value, ConfigDescriptor descriptor)
    {
        switch (descriptor.Type)
        {
            case ConfigValueType.Bool:
                if (value is bool boolValue)
                {
                    return Result<string>.Ok(boolValue ? "true" : "false");
                }
                return Result<string>.Fail($"Expected a boolean for '{descriptor.Key}'");

            case ConfigValueType.String:
                if (value is string stringValue)
                {
                    return Result<string>.Ok(stringValue);
                }
                return Result<string>.Fail($"Expected a string for '{descriptor.Key}'");

            case ConfigValueType.Number:
                if (value is long longValue)
                {
                    return Result<string>.Ok(longValue.ToString(CultureInfo.InvariantCulture));
                }
                if (value is double doubleValue)
                {
                    return Result<string>.Ok(doubleValue.ToString(CultureInfo.InvariantCulture));
                }
                return Result<string>.Fail($"Expected a number for '{descriptor.Key}'");

            case ConfigValueType.Enum:
                if (value is not string enumValue)
                {
                    return Result<string>.Fail($"Expected a string for '{descriptor.Key}'");
                }
                if (!descriptor.Values.Contains(enumValue, StringComparer.Ordinal))
                {
                    var allowedValues = string.Join(", ", descriptor.Values);
                    return Result<string>.Fail($"Value '{enumValue}' for '{descriptor.Key}' is not one of: {allowedValues}");
                }
                return Result<string>.Ok(enumValue);

            case ConfigValueType.StringList:
                if (value is not IReadOnlyList<string> listValue)
                {
                    return Result<string>.Fail($"Expected a list of strings for '{descriptor.Key}'");
                }
                return Result<string>.Ok(JsonSerializer.Serialize(listValue));

            default:
                return Result<string>.Fail($"Unknown descriptor type for '{descriptor.Key}'");
        }
    }
}
