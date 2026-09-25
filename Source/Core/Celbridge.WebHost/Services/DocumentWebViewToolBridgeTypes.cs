using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.WebHost.Services;

// A frame a call acts on, and whether it is the page itself.
internal sealed record ResolvedFrame(string Name, bool IsTop);

// The area of the page a screenshot captures, and the name of the frame it shows.
internal sealed record FrameClip(string Frame, ScreenshotClip Clip);

internal sealed record ConsoleEntry(
    string Level,
    long TimestampMs,
    IReadOnlyList<string> Args,
    string? Stack)
{
    // The frame the entry was logged in. A result names its frame once, so each entry leaves it out.
    [JsonIgnore]
    public string Frame { get; init; } = DocumentWebViewToolBridge.TopFrame;

    public static ConsoleEntry? FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var level = element.TryGetProperty("level", out var levelElement) && levelElement.ValueKind == JsonValueKind.String
            ? levelElement.GetString() ?? "log"
            : "log";

        // Date.now() is integer milliseconds, but V8's JSON serialisation
        // can emit numbers in forms that GetInt64 rejects (scientific notation
        // for very large values, fractional fallback under unusual locales).
        // Prefer TryGetInt64 and fall back to TryGetDouble so a quirky number
        // never aborts the drain.
        long timestamp = 0;
        if (element.TryGetProperty("timestampMs", out var tsElement) &&
            tsElement.ValueKind == JsonValueKind.Number)
        {
            if (!tsElement.TryGetInt64(out timestamp) &&
                tsElement.TryGetDouble(out var tsDouble))
            {
                timestamp = (long)tsDouble;
            }
        }

        var args = new List<string>();
        if (element.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in argsElement.EnumerateArray())
            {
                args.Add(arg.ValueKind == JsonValueKind.String ? arg.GetString() ?? string.Empty : arg.GetRawText());
            }
        }

        var stack = element.TryGetProperty("stack", out var stackElement) && stackElement.ValueKind == JsonValueKind.String
            ? stackElement.GetString()
            : null;

        return new ConsoleEntry(level, timestamp, args, stack)
        {
            Frame = ReadFrame(element)
        };
    }

    // The shim names the frame each entry came from. An entry without a name came from the page itself.
    internal static string ReadFrame(JsonElement element)
    {
        if (element.TryGetProperty("frame", out var frameElement) &&
            frameElement.ValueKind == JsonValueKind.String)
        {
            return frameElement.GetString() ?? DocumentWebViewToolBridge.TopFrame;
        }

        return DocumentWebViewToolBridge.TopFrame;
    }
}

internal sealed record ConsoleSnapshot(
    string Frame,
    IReadOnlyList<ConsoleEntry> Entries,
    int Returned,
    int TotalAccumulated);

internal sealed partial record NetworkBody(string Text, int TruncatedBytes);

internal sealed partial record NetworkEntry(
    long Id,
    string Type,
    string Method,
    string Url,
    int Status,
    long StartTimeMs,
    long DurationMs,
    long RequestSize,
    long ResponseSize,
    IReadOnlyDictionary<string, string>? RequestHeaders,
    IReadOnlyDictionary<string, string>? ResponseHeaders,
    string? RequestBodyDescription,
    NetworkBody? ResponseBody,
    string? Error)
{
    // The frame that made the request.
    public string Frame { get; init; } = DocumentWebViewToolBridge.TopFrame;

    public static NetworkEntry? FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long id = element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number
            ? (idElement.TryGetInt64(out var idLong) ? idLong : 0)
            : 0;

        string type = element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString() ?? "fetch"
            : "fetch";

        string method = element.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String
            ? methodElement.GetString() ?? "GET"
            : "GET";

        string url = element.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
            ? urlElement.GetString() ?? string.Empty
            : string.Empty;

        int status = 0;
        if (element.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Number)
        {
            statusElement.TryGetInt32(out status);
        }

        long startTimeMs = ReadInt64(element, "startTimeMs");
        long durationMs = ReadInt64(element, "durationMs");
        long requestSize = ReadInt64(element, "requestSize");
        long responseSize = ReadInt64(element, "responseSize");

        var requestHeaders = ReadStringDictionary(element, "requestHeaders");
        var responseHeaders = ReadStringDictionary(element, "responseHeaders");

        string? requestBodyDescription = element.TryGetProperty("requestBodyDescription", out var requestBodyElement) && requestBodyElement.ValueKind == JsonValueKind.String
            ? requestBodyElement.GetString()
            : null;

        NetworkBody? responseBody = null;
        if (element.TryGetProperty("responseBody", out var responseBodyElement) && responseBodyElement.ValueKind == JsonValueKind.Object)
        {
            var text = responseBodyElement.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString() ?? string.Empty
                : string.Empty;
            int truncated = 0;
            if (responseBodyElement.TryGetProperty("truncatedBytes", out var truncElement) && truncElement.ValueKind == JsonValueKind.Number)
            {
                truncElement.TryGetInt32(out truncated);
            }
            responseBody = new NetworkBody(text, truncated);
        }

        string? error = element.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : null;

        return new NetworkEntry(id, type, method, url, status, startTimeMs, durationMs,
            requestSize, responseSize, requestHeaders, responseHeaders,
            requestBodyDescription, responseBody, error)
        {
            Frame = ConsoleEntry.ReadFrame(element)
        };
    }

    private static long ReadInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }
        if (element.TryGetInt64(out var asLong)) return asLong;
        if (element.TryGetDouble(out var asDouble)) return (long)asDouble;
        return 0;
    }

    private static IReadOnlyDictionary<string, string>? ReadStringDictionary(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                result[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
            else
            {
                result[prop.Name] = prop.Value.GetRawText();
            }
        }
        return result;
    }
}

internal sealed partial record NetworkEntryView(
    long Id,
    string Type,
    string Method,
    string Url,
    int Status,
    long StartTimeMs,
    long DurationMs,
    long RequestSize,
    long ResponseSize,
    IReadOnlyDictionary<string, string>? RequestHeaders,
    IReadOnlyDictionary<string, string>? ResponseHeaders,
    string? RequestBodyDescription,
    NetworkBody? ResponseBody,
    string? Error);

internal sealed record NetworkSnapshot(
    string Frame,
    IReadOnlyList<NetworkEntryView> Entries,
    int Returned,
    int TotalAccumulated);
