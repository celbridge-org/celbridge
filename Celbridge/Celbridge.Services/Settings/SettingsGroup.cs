using Celbridge.BaseLibrary.Settings;
using Newtonsoft.Json;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace Celbridge.Services.Settings;

public class SettingsGroup : ISettingsGroup
{
    private ILoggingService _loggingService;
    private string? _groupName;
    private IPropertySet _propertySet;

    public SettingsGroup(ILoggingService loggingService)
    {
        _loggingService = loggingService;

        // Ideally we would use the Containers system provided by ApplicationDataContainer, but this is only
        // available on Windows. Instead, we use the root LocalSettings.Value property set, with a prepended group name.
        _propertySet = ApplicationData.Current.LocalSettings.Values;
    }

    public void Initialize(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        if (!string.IsNullOrEmpty(_groupName))
        {
            throw new InvalidOperationException("SettingsGroup has already been initialized.");            
        }

        _groupName = groupName;
    }

    private string GetSettingKey(string key)
    {
        Guard.IsNotNullOrEmpty(_groupName);
        return $"{_groupName}.{key}";
    }

    public void SetValue<T>(string key, T value) 
        where T : notnull
    {
        var settingKey = GetSettingKey(key);

        try
        {
            string json = JsonConvert.SerializeObject(value);
            _propertySet[settingKey] = json;
        }
        catch (JsonException ex)
        {
            _loggingService.Error($"Failed to set setting '{settingKey}' because an exception occured. {ex}");
        }
    }

    public T GetValue<T>(string key, T defaultValue) where T : notnull
    {
        var settingKey = GetSettingKey(key);

        try
        {
            if (!_propertySet.TryGetValue(settingKey, out object? json))
            {
                return defaultValue;
            }

            // Deserialize the JSON string back into the generic type T.
            // The serialization type does not have to match the deserialization type. The deserializer
            // simply attempts to match the property names.This allows for clients to change the serialization
            // type in future releases, as long as the key remains the same.
            var value = JsonConvert.DeserializeObject<T>((string)json);
            if (value is null)
            {
                _loggingService.Error($"Failed to get setting '{settingKey}' because the value failed to deserialize.");
                return defaultValue;
            }

            return value;
        }
        catch (JsonException ex)
        {
            _loggingService.Error($"Failed to get setting '{settingKey}' because a Json exception occured. {ex}");
            return defaultValue;
        }
    }

    public bool ContainsKey(string key)
    {
        var settingKey = GetSettingKey(key);

        return _propertySet.ContainsKey(settingKey);
    }

    public bool ContainsValue<T>(string key, T value)
        where T : notnull
    {
        var settingKey = GetSettingKey(key);

        try
        {
            if (!_propertySet.TryGetValue(settingKey, out object? json))
            {
                return false;
            }

            var v = JsonConvert.DeserializeObject<T>((string)json);
            if (v is null)
            {
                _loggingService.Error($"Failed to get setting '{settingKey}' because the value failed to deserialize.");
                return false;
            }

            return value.Equals(v);
        }
        catch (JsonException ex)
        {
            _loggingService.Error($"Failed to get setting '{settingKey}' because a Json exception occured. {ex}");
            return false;
        }
    }

    public Result DeleteValue(string key)
    {
        var settingKey = GetSettingKey(key);

        if (!_propertySet.Remove(key))
        {
            return Result.Fail($"Failed to remove setting '{settingKey}' because it was not found.");
        }

        return Result.Ok();
    }

    public void Reset()
    {
        // Delete all settings that start with the group name
        var keys = ApplicationData.Current.LocalSettings.Values.Keys;
        foreach (var key in keys)
        {
            if (key.StartsWith($"{_groupName}."))
            {
                ApplicationData.Current.LocalSettings.Values.Remove(key);
            }
        }
    }
}
