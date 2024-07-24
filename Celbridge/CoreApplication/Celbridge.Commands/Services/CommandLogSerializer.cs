using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Celbridge.Commands.Services;

public class CommandLogSerializer : ICommandLogSerializer
{
    private readonly JsonSerializerSettings _jsonSettingsWithProperties;
    private readonly JsonSerializerSettings _jsonSettingsNoProperties;

    public CommandLogSerializer()
    {
        _jsonSettingsWithProperties = CreateJsonSettings(false);
        _jsonSettingsNoProperties = CreateJsonSettings(true);
    }

    public string SerializeObject(object? obj, bool ignoreCommandProperties)
    {
        var jsonSettings = ignoreCommandProperties ? _jsonSettingsNoProperties : _jsonSettingsWithProperties;
        var serialized = JsonConvert.SerializeObject(obj, jsonSettings);

        return serialized;
    }

    private JsonSerializerSettings CreateJsonSettings(bool ignoreCommandProperties)
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CommandSerializerContractResolver(),
            Formatting = Formatting.None
        };

        settings.Converters.Add(new ExecutedCommandJsonConverter(ignoreCommandProperties));
        settings.Converters.Add(new StringEnumConverter());
        settings.Converters.Add(new EntityIdConverter());
        settings.Converters.Add(new ResourceKeyConverter());

        return settings;
    }
}
