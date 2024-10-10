using Celbridge.Foundation;
using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;

namespace Celbridge.Settings.Services;

/// <summary>
/// A non-persistent implementation of ISettingsGroup for use with automated tests and other unpackaged builds.
/// Any properties stored in this class are discarded after the application exits.
/// </summary>
public class TempSettingsGroup : ISettingsGroup
{
    private string? _groupName;
    private Dictionary<string, object> _container = new();

    public void Initialize(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        if (!string.IsNullOrEmpty(_groupName))
        {
            throw new InvalidOperationException("Settings container has already been initialized.");
        }

        _groupName = groupName;
    }

    private string GetSettingKey(string key)
    {
        Guard.IsNotNullOrWhiteSpace(_groupName);
        return $"{_groupName}.{key}";
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        var settingKey = GetSettingKey(key);

        string json = JsonConvert.SerializeObject(value);
        _container[settingKey] = json;
    }

    public T GetValue<T>(string key, T defaultValue) where T : notnull
    {
        var settingKey = GetSettingKey(key);

        if (!_container.TryGetValue(settingKey, out object? json))
        {
            return defaultValue;
        }

        var value = JsonConvert.DeserializeObject<T>((string)json);
        if (value is null)
        {
            throw new InvalidOperationException();
        }

        return value;
    }

    public bool ContainsKey(string key)
    {
        var settingKey = GetSettingKey(key);

        return _container.ContainsKey(settingKey);
    }

    public bool ContainsValue<T>(string key, T value)
        where T : notnull
    {
        var settingKey = GetSettingKey(key);

        if (_container.TryGetValue(settingKey, out var v))
        {
            return v.Equals(value);
        }

        return false;
    }

    public Result DeleteValue(string key)
    {
        var settingKey = GetSettingKey(key);

        if (!_container.Remove(settingKey))
        {
            return Result.Fail($"Failed to remove setting '{settingKey}' because it was not found.");
        }

        return Result.Ok();
    }

    public void Reset()
    {
        _container.Clear();
    }
}
