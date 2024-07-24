using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Celbridge.Commands.Services;

public class CommandLogSerializer : ICommandLogSerializer
{
    private readonly JsonSerializerSettings _settings;

    public CommandLogSerializer()
    {
        _settings = CreateJsonSettings();
    }

    public string SerializeObject(object? obj)
    {
        var serialized = JsonConvert.SerializeObject(obj, _settings);
        return serialized;
    }

    private record ExecutionInfo(float ElapsedTime, CommandExecutionMode ExecutionMode);
    private record CommandItem(string CommandName, IExecutableCommand Command, ExecutionInfo ExecutionInfo);
    public string SerializeExecutedCommand(ExecutedCommandMessage message)
    {
        var commandName = message.Command.GetType().Name;
        var executionInfo = new ExecutionInfo(message.ElapsedTime, message.ExecutionMode);
        var commandItem = new CommandItem(commandName, message.Command, executionInfo);

        var serialized = SerializeObject(commandItem);
        return serialized;
    }

    private JsonSerializerSettings CreateJsonSettings()
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CommandSerializerContractResolver(),
            Formatting = Formatting.None
        };

        settings.Converters.Add(new StringEnumConverter());
        settings.Converters.Add(new EntityIdConverter());
        settings.Converters.Add(new ResourceKeyConverter());

        return settings;
    }
}
