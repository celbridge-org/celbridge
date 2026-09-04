using System.Text.Json;

namespace Celbridge.WebHost;

/// <summary>
/// A JSON-RPC style notification a hosted page posted to its host over the native web message bus.
/// </summary>
internal sealed record WebMessageNotification(string Method, JsonElement Parameters);

/// <summary>
/// Reads the notifications hosted pages post over the native web message bus, which carries the signals that
/// cannot go through the JSON-RPC channel: they come from scripts the host injects into pages it did not
/// author, so there is no client library on the other end.
/// </summary>
internal static class WebMessageEnvelope
{
    /// <summary>
    /// Reads a notification naming one of the given methods, or null for any other message. Every message a
    /// surface sends its host arrives on the same event, including editor content, so a message that merely
    /// mentions a method name must not be mistaken for that method.
    /// </summary>
    public static WebMessageNotification? TryRead(string message, params string[] methods)
    {
        try
        {
            var envelope = UnwrapPageJson(message);
            if (string.IsNullOrEmpty(envelope))
            {
                return null;
            }

            using var envelopeDocument = JsonDocument.Parse(envelope);

            return ReadNotification(envelopeDocument.RootElement, methods);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The JSON a page produced, unwrapped from the encoding its head applied. A page hands JSON over as a JS
    /// string, which the WebView2 heads deliver wrapped in a JSON string literal while the macOS head delivers
    /// the text itself. Returns null when the text is not JSON at all.
    /// </summary>
    public static string? UnwrapPageJson(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString()
                : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WebMessageNotification? ReadNotification(JsonElement element, string[] methods)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var method = methodElement.GetString();
        if (method is null
            || !methods.Contains(method, StringComparer.Ordinal))
        {
            return null;
        }

        // Cloned because the JsonDocument that owns this element is disposed as this method returns.
        var parameters = element.TryGetProperty("params", out var parametersElement)
            ? parametersElement.Clone()
            : default;

        return new WebMessageNotification(method, parameters);
    }

    /// <summary>
    /// Reads a string property from a notification's parameters, or null when it is absent.
    /// </summary>
    public static string? ReadString(JsonElement parameters, string propertyName)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
