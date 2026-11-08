using System.Text.Json;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Builds the DevTools protocol's Runtime.evaluate request for a host script, and reads its response. The packaged
/// Windows head runs host scripts this way. ExecuteScriptAsync would give the page transient user activation for 5 s.
/// During those 5 s, a pop-up opened by the page would count as opened by the user.
/// </summary>
internal static class DevToolsEvaluation
{
    public const string MethodName = "Runtime.evaluate";

    private const string NullJson = "null";

    /// <summary>
    /// Builds the request's parameters. The page encodes the script's value, and the request sets userGesture to false.
    /// </summary>
    public static string BuildEvaluateParams(string expression)
    {
        var parameters = new Dictionary<string, object>
        {
            ["expression"] = ScriptValueJson.Wrap(expression),
            ["returnByValue"] = true,
            ["userGesture"] = false
        };

        return JsonSerializer.Serialize(parameters);
    }

    /// <summary>
    /// Returns the page's JSON. A script that throws reads as null.
    /// </summary>
    public static string ReadEvaluateResult(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        var root = response.RootElement;

        if (root.TryGetProperty("exceptionDetails", out _)
            || !root.TryGetProperty("result", out var result)
            || !result.TryGetProperty("value", out var value))
        {
            return NullJson;
        }

        return ScriptValueJson.Unwrap(value.GetRawText());
    }
}
