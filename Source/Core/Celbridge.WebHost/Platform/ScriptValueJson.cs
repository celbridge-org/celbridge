using System.Text.Json;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A wrapper that makes the page return a host script's value as JSON. A value reads the same on every head, because
/// the page does the encoding. The JSON arrives as the only string in an array. A value with no JSON form, for
/// example undefined, NaN or a function, reads as null.
/// </summary>
internal static class ScriptValueJson
{
    private const string NullJson = "null";

    /// <summary>
    /// Wraps an expression so that the page returns its value as JSON, inside an array. A BigInt, or a value that
    /// refers back to itself, for example window, makes the wrapped script throw.
    /// </summary>
    // UNO-BUG: On macOS, Uno encodes the result with NSJSONSerialization. A value that refers to itself, for example
    // window, makes NSJSONSerialization recurse until the main thread's stack overflows, and the application hangs.
    // Uno also escapes the quotes in a returned string but not its backslashes. NSJSONSerialization escapes an array
    // correctly, so the page's JSON comes back inside an array.
    public static string Wrap(string expression)
    {
        // A trailing semicolon is not allowed inside the parentheses. The line breaks stop a trailing line
        // comment from hiding the closing parentheses.
        var trimmedExpression = expression.TrimEnd().TrimEnd(';');

        return $"[JSON.stringify((\n{trimmedExpression}\n)) ?? null]";
    }

    /// <summary>
    /// Takes the page's JSON out of its array. A result that is not an array of one string reads as null.
    /// </summary>
    public static string Unwrap(string? result)
    {
        if (string.IsNullOrEmpty(result))
        {
            return NullJson;
        }

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array ||
            root.GetArrayLength() != 1 ||
            root[0].ValueKind != JsonValueKind.String)
        {
            return NullJson;
        }

        return root[0].GetString() ?? NullJson;
    }
}
