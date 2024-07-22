using Celbridge.Resources;
using Newtonsoft.Json;

namespace Celbridge.Commands.Services;

public class ResourceKeyConverter : JsonConverter<ResourceKey>
{
    public override void WriteJson(JsonWriter writer, ResourceKey value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString());
    }

    public override ResourceKey ReadJson(JsonReader reader, Type objectType, ResourceKey existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var key = reader.Value!.ToString()!;
        return new ResourceKey(key);
    }
}
