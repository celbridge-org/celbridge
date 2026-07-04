using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Logging.Services;

public class LogSerializer : ILogSerializer
{
    // Command arguments longer than this are truncated in the log. Large enough to keep a useful
    // snippet of an edit's match text, small enough that a whole-file write does not flood the log.
    private const int MaxLoggedStringLength = 256;

    private readonly JsonSerializerOptions _jsonSettingsWithProperties;
    private readonly JsonSerializerOptions _jsonSettingsNoProperties;

    public LogSerializer()
    {
        _jsonSettingsWithProperties = CreateJsonSettings(false);
        _jsonSettingsNoProperties = CreateJsonSettings(true);
    }

    public string SerializeObject(object? obj, bool ignoreCommandProperties)
    {
        var jsonSettings = ignoreCommandProperties ? _jsonSettingsNoProperties : _jsonSettingsWithProperties;
        var serialized = JsonSerializer.Serialize(obj, jsonSettings);

        return serialized;
    }

    private JsonSerializerOptions CreateJsonSettings(bool ignoreCommandProperties)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new ExecuteCommandStartedMessageJsonConverter(ignoreCommandProperties));
        options.Converters.Add(new TruncatingStringJsonConverter(MaxLoggedStringLength));
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new EntityIdConverter());
        options.Converters.Add(new ResourceKeyConverter());

        return options;
    }
}
