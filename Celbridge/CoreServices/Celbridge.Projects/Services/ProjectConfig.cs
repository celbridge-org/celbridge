using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Celbridge.Projects.Services;

public class ProjectConfig : IProjectConfig
{
    private readonly Dictionary<string, string> _properties = new();

    public Result Initialize(string jsonContent)
    {
        try
        {
            var jsonObject = JObject.Parse(jsonContent);

            foreach (var property in jsonObject.Properties())
            {
                if (property.Value.Type == JTokenType.String ||
                    property.Value.Type == JTokenType.Integer ||
                    property.Value.Type == JTokenType.Float ||
                    property.Value.Type == JTokenType.Boolean ||
                    property.Value.Type == JTokenType.Null)
                {
                    // Convert primitive types to their string representations
                    _properties[property.Name] = property.Value.ToString();
                }
                else
                {
                    // Serialize to JSON string
                    _properties[property.Name] = property.Value.ToString(Formatting.None);
                }
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when populating the project properties")
                .WithException(ex);
        }
    }

    public string GetProperty(string propertyName, string defaultValue)
    {
        if (_properties.TryGetValue(propertyName, out string? value))
        {
            return value ?? string.Empty;
        }

        return defaultValue;
    }

    public string GetProperty(string propertyName)
    {
        return GetProperty(propertyName, string.Empty);
    }

    public void SetProperty(string propertyName, string jsonEncodedValue)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            // Setting an empty string key is a noop
            return;
        }
        _properties[propertyName] = jsonEncodedValue;
    }

    public bool HasProperty(string propertyName)
    {
        return _properties.ContainsKey(propertyName);
    }
}

