using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Logging.Services;

/// <summary>
/// Serializes strings for logging, truncating any value longer than the limit to its head plus a
/// character count. Keeps file-editing commands (whose Content/OldString/NewString carry whole
/// documents) from dumping their entire payload into the log.
/// </summary>
public class TruncatingStringJsonConverter : JsonConverter<string>
{
    private readonly int _maxLength;

    public TruncatingStringJsonConverter(int maxLength)
    {
        _maxLength = maxLength;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (value.Length <= _maxLength)
        {
            writer.WriteStringValue(value);
            return;
        }

        var truncated = $"{value.Substring(0, _maxLength)}... ({value.Length} chars)";
        writer.WriteStringValue(truncated);
    }

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() ?? string.Empty;
    }
}
