namespace Celbridge.BaseLibrary.Settings;

/// <summary>
/// A platform abstraction layer for reading and writing setting values to persistant storage.
/// </summary>
public interface IApplicationSettings
{
    /// <summary>
    /// Stores a value with the provided key.
    /// </summary>
    Result SetValue<T>(string settingKey, T value) where T : notnull;

    /// <summary>
    /// Returns a previously stored value with the provided key. <summary>
    /// Fails if the requested value was not found or could not be deserialized to the
    /// requested type.
    /// </summary>
    Result<T> GetValue<T>(string settingKey) where T : notnull;
}
