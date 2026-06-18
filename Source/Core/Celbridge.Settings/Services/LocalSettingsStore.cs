using System.Text.Json;
using Windows.Foundation.Collections;

namespace Celbridge.Settings.Services;

using ISettingsLogger = Logging.ILogger<LocalSettingsStore>;

/// <summary>
/// Persists settings as key/value properties using the Uno Platform Storage API
/// (Windows LocalSettings). The Storage API is not available in unpackaged Windows
/// builds; use InMemorySettingsStore as a non-persistent replacement there.
/// </summary>
internal sealed class LocalSettingsStore : IApplicationSettingsStore
{
    private readonly ISettingsLogger _logger;
    private readonly IPropertySet _propertySet;

    public LocalSettingsStore(ISettingsLogger logger)
    {
        _logger = logger;

        // ApplicationDataContainer's Containers system is Windows-only, so we use
        // the root LocalSettings property set directly with self-namespaced keys.
        _propertySet = ApplicationData.Current.LocalSettings.Values;
    }

    public T GetValue<T>(string key, T defaultValue) where T : notnull
    {
        try
        {
            if (!_propertySet.TryGetValue(key, out object? json))
            {
                return defaultValue;
            }

            // The serialized type need not match the deserialized type; the
            // deserializer matches by property name, so the stored shape can
            // evolve as long as the key stays the same.
            var value = JsonSerializer.Deserialize<T>((string)json);
            if (value is null)
            {
                _logger.LogError($"Failed to get setting '{key}' because the value failed to deserialize.");
                return defaultValue;
            }

            return value;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, $"Failed to get setting '{key}'.");
            return defaultValue;
        }
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        try
        {
            string json = JsonSerializer.Serialize(value);
            _propertySet[key] = json;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, $"Failed to set setting '{key}'.");
        }
    }

    public bool ContainsKey(string key)
    {
        return _propertySet.ContainsKey(key);
    }

    public void RemoveValue(string key)
    {
        _propertySet.Remove(key);
    }
}
