using Celbridge.Commands;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Celbridge.Logging.Services;

public class CommandExecutingMessageJsonConverter : JsonConverter<CommandExecutingMessage>
{
    public bool _ignoreCommandProperties { get; set; } = false;

    public CommandExecutingMessageJsonConverter(bool ignoreCommandProperties)
    {
        _ignoreCommandProperties = ignoreCommandProperties;
    }

    public override void WriteJson(JsonWriter writer, CommandExecutingMessage? message, JsonSerializer serializer)
    {
        Guard.IsNotNull(message);

        var command = message.Command;

        var outputJO = new JObject();
        if (!_ignoreCommandProperties)
        {
            JObject commandJO = JObject.FromObject(command, serializer);
            foreach (var kv in commandJO)
            {
                outputJO.Add(kv.Key, kv.Value);
            }
        }

        outputJO.Add("_CommandId", JToken.FromObject(command.CommandId, serializer));
        outputJO.Add("_UndoGroupId", JToken.FromObject(command.UndoGroupId, serializer));
        outputJO.Add("_CommandFlags", JToken.FromObject(command.CommandFlags, serializer));
        outputJO.Add("_Source", JToken.FromObject(command.ExecutionSource, serializer));

        outputJO.WriteTo(writer);
    }

    public override CommandExecutingMessage ReadJson(JsonReader reader, Type objectType, CommandExecutingMessage? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}
