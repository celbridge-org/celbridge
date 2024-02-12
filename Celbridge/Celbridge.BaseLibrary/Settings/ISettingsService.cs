namespace Celbridge.BaseLibrary.Settings;

/// <summary>
/// Read and write user settings that persist between application sessions.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Associates a value with this key in the container.
    /// </summary>
    Result SetValue<T>(string containerName, string key, T value) where T : notnull;

    /// <summary>
    /// Gets a previously stored value with the provided key from the container.
    /// Fails if the requested value was not found.
    /// </summary>
    Result<T> GetValue<T>(string containerName, string key) where T : notnull;

    /// <summary>
    /// Returns true if the container contains the key.
    /// </summary>
    bool ContainsValue(string containerName, string key);

    /// <summary>
    /// Deletes a key from the container.
    /// Fails if the container does not contain the key.
    /// </summary>
    Result DeleteValue(string containerName, string key);

    /// <summary>
    /// Deletes all keys & values from the container.
    /// </summary>
    void DeleteAll(string containerName);
}