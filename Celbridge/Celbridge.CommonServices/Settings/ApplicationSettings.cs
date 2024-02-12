using Windows.Storage;
using Newtonsoft.Json;
using CommunityToolkit.Diagnostics;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.Core;

namespace Celbridge.CommonServices.Settings;

public class ApplicationSettings : IApplicationSettings
{
    public Result SetValue<T>(string containerName, string key, T value) where T : notnull
    {
        if (string.IsNullOrEmpty(containerName))
        {
            return Result.Fail($"Failed to set setting '{containerName}.{key}' because container name was not specified.");
        }

        try
        {
            string json = JsonConvert.SerializeObject(value);

            var container = ApplicationData.Current.LocalSettings.CreateContainer(containerName, ApplicationDataCreateDisposition.Always);
            Guard.IsNotNull(container);                                
            container.Values[key] = json;

            return Result.Ok();
        }
        catch (JsonException ex)
        {
            return Result<T>.Fail($"Failed to set setting '{containerName}.{key}' because an exception occured. {ex}");
        }
    }

    public Result<T> GetValue<T>(string containerName, string key) where T : notnull
    {
        if (string.IsNullOrEmpty(containerName))
        {
            return Result<T>.Fail($"Failed to set setting '{containerName}.{key}' because container name was not specified.");
        }

        try
        {
            var container = ApplicationData.Current.LocalSettings.CreateContainer(containerName, ApplicationDataCreateDisposition.Always);
            Guard.IsNotNull(container);

            if (!container.Values.TryGetValue(key, out object? json))
            {
                return Result<T>.Fail($"Failed to get setting '{containerName}.{key}' because the value was not found.");
            }

            // Deserialize the JSON string back into the specified type T.
            // The serialization type does not have to match the deserialization type, the deserializer
            // simply matches property names.
            // This allows for clients to change the serialization type in future releases, as long
            // as the settingsKey remains the same.
            var value = JsonConvert.DeserializeObject<T>((string)json);
            if (value is null)
            {
                return Result<T>.Fail($"Failed to get setting '{containerName}.{key}' because the value was null.");
            }

            return Result<T>.Ok(value);
        }
        catch (JsonException ex)
        {
            return Result<T>.Fail($"Failed to get setting '{containerName}.{key}' because an exception occured. {ex}");
        }
    }

    public bool ContainsValue(string containerName, string key) 
    {
        var container = ApplicationData.Current.LocalSettings.CreateContainer(containerName, ApplicationDataCreateDisposition.Always);
        Guard.IsNotNull(container);

        return container.Values.ContainsKey(key);
    }

    public Result DeleteValue(string containerName, string key)
    {
        var container = ApplicationData.Current.LocalSettings.CreateContainer(containerName, ApplicationDataCreateDisposition.Always);
        Guard.IsNotNull(container);

        if (!container.Values.Remove(key))
        {
            return Result.Fail($"Failed to remove setting '{containerName}.{key}' because it was not found.");
        }

        return Result.Ok();
    }

    public void DeleteAll(string containerName)
    {
        ApplicationData.Current.LocalSettings.DeleteContainer(containerName);
    }
}
