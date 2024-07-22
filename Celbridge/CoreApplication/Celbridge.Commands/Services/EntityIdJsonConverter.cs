using Celbridge.Utilities;
using Newtonsoft.Json;

namespace Celbridge.Commands.Services;

public class EntityIdConverter : JsonConverter<EntityId>
{
    public override void WriteJson(JsonWriter writer, EntityId value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString());
    }

    public override EntityId ReadJson(JsonReader reader, Type objectType, EntityId existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var idText = reader.Value!.ToString();

        if (ulong.TryParse(idText, out ulong id))
        {
            return new EntityId(id);
        }

        return EntityId.InvalidId;
    }
}
