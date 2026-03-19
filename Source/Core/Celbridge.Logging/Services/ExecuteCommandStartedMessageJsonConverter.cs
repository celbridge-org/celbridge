using Celbridge.Commands;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Logging.Services;

public class ExecuteCommandStartedMessageJsonConverter : JsonConverter<ExecuteCommandStartedMessage>
{
    public bool _ignoreCommandProperties { get; set; } = false;

    public ExecuteCommandStartedMessageJsonConverter(bool ignoreCommandProperties)
    {
        _ignoreCommandProperties = ignoreCommandProperties;
    }

    public override void Write(Utf8JsonWriter writer, ExecuteCommandStartedMessage message, JsonSerializerOptions options)
    {
        Guard.IsNotNull(message);

        var command = message.Command;

        writer.WriteStartObject();

        writer.WritePropertyName("_CommandType");
        var commandTypeString = command.GetType().ToString();
        JsonSerializer.Serialize(writer, commandTypeString, options);

        if (!_ignoreCommandProperties)
        {
            // Serialize command properties (excluding the base IExecutableCommand properties)
            var properties = command.GetType().GetProperties()
                .Where(p => p.DeclaringType != typeof(object) &&
                           p.Name != nameof(IExecutableCommand.CommandId) &&
                           p.Name != nameof(IExecutableCommand.CommandFlags) &&
                           p.Name != nameof(IExecutableCommand.ExecutionSource) &&
                           p.Name != nameof(IExecutableCommand.OnExecute) &&
                           p.CanRead);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(command);
                if (value is not null || options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull)
                {
                    writer.WritePropertyName(prop.Name);
                    JsonSerializer.Serialize(writer, value, prop.PropertyType, options);
                }
            }
        }

        // Add the command metadata properties at the end
        writer.WritePropertyName("_CommandId");
        JsonSerializer.Serialize(writer, command.CommandId, options);

        writer.WritePropertyName("_CommandFlags");
        JsonSerializer.Serialize(writer, command.CommandFlags, options);

        writer.WritePropertyName("_Source");
        JsonSerializer.Serialize(writer, command.ExecutionSource, options);

        writer.WriteEndObject();
    }

    public override ExecuteCommandStartedMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
