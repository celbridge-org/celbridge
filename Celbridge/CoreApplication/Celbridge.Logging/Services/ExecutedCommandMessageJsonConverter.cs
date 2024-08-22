using Celbridge.Commands;
using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Celbridge.Logging.Services;

public class ExecutedCommandMessageJsonConverter : JsonConverter<ExecutedCommandMessage>
{
    public bool _ignoreCommandProperties { get; set; } = false;

    public ExecutedCommandMessageJsonConverter(bool ignoreCommandProperties)
    {
        _ignoreCommandProperties = ignoreCommandProperties;
    }

    public override void WriteJson(JsonWriter writer, ExecutedCommandMessage? value, JsonSerializer serializer)
    {
        Guard.IsNotNull(value);

        var command = value.Command;
        var commandName = value.Command.GetType().Name;

        var outputJO = new JObject();
        // outputJO.Add("CommandName", JToken.FromObject(commandName, serializer));

        if (!_ignoreCommandProperties)
        {
            JObject commandJO = JObject.FromObject(command, serializer);
            foreach (var kv in commandJO)
            {
                outputJO.Add(kv.Key, kv.Value);
            }
        }

        // Format the elapsed time to 2 decimal places
        var elapsedTime = value.ElapsedTime.ToString("F2");

        outputJO.Add("_ExecutionMode", JToken.FromObject(value.ExecutionMode, serializer));
        outputJO.Add("_ElapsedTime", JToken.FromObject(elapsedTime, serializer));
        outputJO.Add("_Source", JToken.FromObject(command.ExecutionSource, serializer));
        outputJO.Add("_UndoStack", JToken.FromObject(command.UndoStackName, serializer));
        outputJO.Add("_CommandId", JToken.FromObject(command.CommandId, serializer));
        outputJO.Add("_UndoGroupId", JToken.FromObject(command.UndoGroupId, serializer));

        outputJO.WriteTo(writer);
    }

    public override ExecutedCommandMessage ReadJson(JsonReader reader, Type objectType, ExecutedCommandMessage? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}
