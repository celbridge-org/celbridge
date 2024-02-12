using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Settings;
using Newtonsoft.Json;
using Windows.Storage;

namespace Celbridge.CommonServices.Settings;

public class SettingsContainer : ISettingsContainer
{
    private ILoggingService _loggingService;
    private string? _containerName;

    public SettingsContainer(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public void Initialize(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentNullException(nameof(containerName));
        }

        if (!string.IsNullOrEmpty(_containerName))
        {
            throw new InvalidOperationException("Settings container has already been initialized.");            
        }

        _containerName = containerName;
    }

    private ApplicationDataContainer GetContainer()
    {
        if (string.IsNullOrEmpty(_containerName))
        {
            throw new InvalidOperationException("Settings container has not been initialized.");
        }

        // We attempt to create the container on every request because it's possible that some other code
        // has deleted the container since the previous request. This is safer but potentially expensive.

        return ApplicationData.Current.LocalSettings.CreateContainer(_containerName, ApplicationDataCreateDisposition.Always);
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        var container = GetContainer();

        try
        {
            string json = JsonConvert.SerializeObject(value);
            container.Values[key] = json;
        }
        catch (JsonException ex)
        {
            _loggingService.Error($"Failed to set setting '{container.Name}.{key}' because an exception occured. {ex}");
        }
    }

    public T GetValue<T>(string key) where T : notnull
    {
        var container = GetContainer();

        try
        {
            if (!container.Values.TryGetValue(key, out object? json))
            {
                return default(T) ?? throw new InvalidOperationException();
            }

            // Deserialize the JSON string back into the generic type T.
            // The serialization type does not have to match the deserialization type. The deserializer
            // simply attempts to match the property names.This allows for clients to change the serialization
            // type in future releases, as long as the key remains the same.
            var value = JsonConvert.DeserializeObject<T>((string)json);
            if (value is null)
            {
                _loggingService.Error($"Failed to get setting '{container.Name}.{key}' because the value failed to deserialize.");
                return default(T) ?? throw new InvalidOperationException();
            }

            return value;
        }
        catch (JsonException ex)
        {
            _loggingService.Error($"Failed to get setting '{container.Name}.{key}' because the Json exception occured. {ex}");
            return default(T) ?? throw new InvalidOperationException();
        }
    }

    public bool ContainsValue(string key)
    {
        var container = GetContainer();
        return container.Values.ContainsKey(key);
    }

    public Result DeleteValue(string key)
    {
        var container = GetContainer();
        if (!container.Values.Remove(key))
        {
            return Result.Fail($"Failed to remove setting '{container.Name}.{key}' because it was not found.");
        }

        return Result.Ok();
    }

    public void Reset()
    {
        ApplicationData.Current.LocalSettings.DeleteContainer(_containerName);
    }
}
