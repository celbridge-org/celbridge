using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Celbridge.Commands.Services;

public class ExecutedCommandJsonConverter : JsonConverter<ExecutedCommandMessage>
{
    public bool _ignoreCommandProperties { get; set; } = false;

    public ExecutedCommandJsonConverter(bool ignoreCommandProperties)
    {
        _ignoreCommandProperties = ignoreCommandProperties;
    }

    public override void WriteJson(JsonWriter writer, ExecutedCommandMessage? value, JsonSerializer serializer)
    {
        Guard.IsNotNull(value);

        var command = value.Command;
        var commandName = value.Command.GetType().Name;

        var outputJO = new JObject();
        outputJO.Add("CommandName", JToken.FromObject(commandName, serializer));

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

        outputJO.Add("ExecutionMode", JToken.FromObject(value.ExecutionMode, serializer));
        outputJO.Add("ElapsedTime", JToken.FromObject(elapsedTime, serializer));
        outputJO.Add("CommandId", JToken.FromObject(command.CommandId, serializer));
        outputJO.Add("Source", JToken.FromObject(command.ExecutionSource, serializer));
        outputJO.Add("UndoStack", JToken.FromObject(command.UndoStackName, serializer));
        outputJO.Add("UndoGroupId", JToken.FromObject(command.UndoGroupId, serializer));

        outputJO.WriteTo(writer);
    }

    public override ExecutedCommandMessage ReadJson(JsonReader reader, Type objectType, ExecutedCommandMessage? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}
