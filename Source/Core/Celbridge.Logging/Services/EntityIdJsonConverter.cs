using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Logging.Services;

public class EntityIdConverter : JsonConverter<EntityId>
{
    public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var idText = reader.GetString();

        if (idText is not null && ulong.TryParse(idText, out ulong id))
        {
            return new EntityId(id);
        }

        return EntityId.InvalidId;
    }
}
