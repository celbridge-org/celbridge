using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Logging.Services;

public class CommandIdConverter : JsonConverter<CommandId>
{
    public override void Write(Utf8JsonWriter writer, CommandId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override CommandId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var idText = reader.GetString();

        if (idText is not null && ulong.TryParse(idText, out ulong id))
        {
            return new CommandId(id);
        }

        return CommandId.InvalidId;
    }
}
