using System.Text.Json;

namespace Celbridge.Settings.Services;

/// <summary>
/// Non-persistent settings store for automated tests and unpackaged builds. Any
/// values stored here are discarded when the application exits.
/// </summary>
internal sealed class InMemorySettingsStore : IApplicationSettingsStore
{
    private readonly Dictionary<string, string> _container = new();

    public T GetValue<T>(string key, T defaultValue) where T : notnull
    {
        if (!_container.TryGetValue(key, out var json))
        {
            return defaultValue;
        }

        var value = JsonSerializer.Deserialize<T>(json);
        if (value is null)
        {
            return defaultValue;
        }

        return value;
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        _container[key] = JsonSerializer.Serialize(value);
    }

    public bool ContainsKey(string key)
    {
        return _container.ContainsKey(key);
    }

    public void RemoveValue(string key)
    {
        _container.Remove(key);
    }
}
