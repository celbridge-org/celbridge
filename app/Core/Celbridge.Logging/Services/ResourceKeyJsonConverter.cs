using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Logging.Services;

public class ResourceKeyConverter : JsonConverter<ResourceKey>
{
    public override void Write(Utf8JsonWriter writer, ResourceKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override ResourceKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var key = reader.GetString()!;
        return new ResourceKey(key);
    }
}
