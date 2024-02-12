using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Settings;
using Newtonsoft.Json;
using Windows.Storage;

namespace Celbridge.CommonServices.Settings;
public class ApplicationSettings : IApplicationSettings
{
    public const string DefaultLocalSettingsKey = "Celbridge.Settings";

    public Result SetValue<T>(string settingKey, T value) where T : notnull
    {
        try
        {
            string json = JsonConvert.SerializeObject(value);
            ApplicationData.Current.LocalSettings.Values[settingKey] = json;
            return Result.Ok();
        }
        catch (JsonException ex)
        {
            return Result<T>.Fail($"Failed to set setting '{settingKey}' because an exception occured. {ex}");
        }
    }

    public Result<T> GetValue<T>(string settingKey) where T : notnull
    {
        try
        {
            // Check if the setting exists
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(settingKey, out object? json))
            {
                // Deserialize the JSON string back into the specified type T.
                // The serialization type does not have to match the deserialization type, the deserializer
                // simply matches property names.
                // This allows for clients to change the serialization type in future releases, as long
                // as the settingsKey remains the same.
                var value = JsonConvert.DeserializeObject<T>((string)json);
                if (value != null)
                {
                    return Result<T>.Ok(value);
                }
            }
        }
        catch (JsonException ex)
        {
            return Result<T>.Fail($"Failed to get setting '{settingKey}' because an exception occured. {ex}");
        }

        return Result<T>.Fail($"Failed to get setting '{settingKey}' because the value was not found or was null.");
    }
}
