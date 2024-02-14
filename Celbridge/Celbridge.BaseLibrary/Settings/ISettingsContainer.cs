namespace Celbridge.BaseLibrary.Settings;

/// <summary>
/// A container for a set of peristent user settings stored as key value pairs.
/// </summary>
public interface ISettingsContainer
{
    /// <summary>
    /// Setup the settings container with a container name.
    /// </summary>
    public void Initialize(string containerName);

    /// <summary>
    /// Associate some value with this key.
    /// </summary>
    void SetValue<T>(string key, T value) where T : notnull;

    /// <summary>
    /// Gets a previously stored value with the provided key.
    /// Returns default(T) if the key does not exist.
    /// </summary>
    T GetValue<T>(string key) where T : notnull;

    /// <summary>
    /// Gets a previously stored value with the provided key.
    /// Returns defaultValue if the key does not exist.
    /// </summary>
    T GetValue<T>(string key, T defaultValue) where T : notnull;

    /// <summary>
    /// Returns true if the container contains the key.
    /// </summary>
    bool ContainsKey(string key);

    /// <summary>
    /// Deletes a key value pair from the container.
    /// Fails if the container does not contain the key.
    /// </summary>
    Result DeleteValue(string key);

    /// <summary>
    /// Deletes all key value pairs from the container.
    /// </summary>
    void Reset();
}