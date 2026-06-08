using System.Text.Json;

namespace Celbridge.WebHost;

/// <summary>
/// Storage shape for a .webview file: a single JSON object carrying the URL
/// the embedded WebView should navigate to.
/// </summary>
public sealed record WebViewFileContent(string SourceUrl)
{
    private const string SourceUrlPropertyName = "sourceUrl";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Parses the JSON body of a .webview file. An empty file or a missing
    /// sourceUrl property returns content with an empty URL rather than a
    /// failure, so a brand-new file (or a hand-edited blank file) still loads.
    /// </summary>
    public static Result<WebViewFileContent> TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new WebViewFileContent(string.Empty);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Result.Fail("Top-level JSON value must be an object.");
        }

        if (!root.TryGetProperty(SourceUrlPropertyName, out var urlElement))
        {
            return new WebViewFileContent(string.Empty);
        }

        if (urlElement.ValueKind == JsonValueKind.Null)
        {
            return new WebViewFileContent(string.Empty);
        }

        if (urlElement.ValueKind != JsonValueKind.String)
        {
            return Result.Fail($"Property '{SourceUrlPropertyName}' must be a string.");
        }

        return new WebViewFileContent(urlElement.GetString() ?? string.Empty);
    }

    /// <summary>
    /// Serialises this content as the canonical .webview JSON document.
    /// Trailing newline matches the convention used by the other text-storage
    /// roundtrips.
    /// </summary>
    public string ToJson()
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SourceUrlPropertyName] = SourceUrl,
        };
        return JsonSerializer.Serialize(payload, WriteOptions) + "\n";
    }
}
