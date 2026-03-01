using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// JS message with no payload, serialized and posted to a WebView.
/// </summary>
public record JsMessage(string Type);

/// <summary>
/// JS message with a typed payload, serialized and posted to a WebView.
/// </summary>
public record JsPayloadMessage<T>(string Type, T Payload);

/// <summary>
/// Helper for sending typed JSON messages to a WebView2 control.
/// Serializes messages with camelCase property naming to match JS conventions.
/// </summary>
[Obsolete("Use WebViewBridge instead. This class will be removed after all editors are migrated.")]
public class WebView2Messenger
{
    // Payload for the set-localization message
    private record LocalizationPayload(Dictionary<string, string> Strings);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CoreWebView2 _coreWebView2;

    public WebView2Messenger(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
    }

    /// <summary>
    /// Serializes the message to camelCase JSON and posts it to the WebView.
    /// </summary>
    public void Send(object message)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        _coreWebView2.PostWebMessageAsString(json);
    }

    /// <summary>
    /// Sends localized strings matching a key prefix from the .NET host application to the WebView.
    /// </summary>
    public void SendLocalizationStrings(IStringLocalizer stringLocalizer, string keyPrefix)
    {
        var assembly = typeof(WebView2Messenger).Assembly;
        using var stream = assembly.GetManifestResourceStream("Celbridge.Strings.Resources.resw");
        Guard.IsNotNull(stream);

        var reswDoc = XDocument.Load(stream);
        var strings = new Dictionary<string, string>();
        foreach (var data in reswDoc.Descendants("data"))
        {
            var name = data.Attribute("name")?.Value;
            if (name is not null && name.StartsWith(keyPrefix))
            {
                strings[name] = stringLocalizer.GetString(name);
            }
        }

        var payload = new LocalizationPayload(strings);
        var message = new JsPayloadMessage<LocalizationPayload>("set-localization", payload);
        Send(message);
    }
}
