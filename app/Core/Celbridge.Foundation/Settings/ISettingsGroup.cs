namespace Celbridge.Settings;

/// <summary>
/// A named group of peristent user settings stored as key value pairs.
/// </summary>
public interface ISettingsGroup
{
    /// <summary>
    /// Setup the settings group with a group name.
    /// </summary>
    public void Initialize(string groupName);

    /// <summary>
    /// Associate some value with this key.
    /// </summary>
    void SetValue<T>(string key, T value) 
        where T : notnull;

    /// <summary>
    /// Gets a previously stored value with the provided key.
    /// Returns defaultValue if the key does not exist.
    /// </summary>
    T GetValue<T>(string key, T defaultValue) 
        where T : notnull;

    /// <summary>
    /// Returns true if the group contains the key.
    /// </summary>
    bool ContainsKey(string key);

    /// <summary>
    /// Returns true if the group contains a key with the specified value.
    /// </summary>
    public bool ContainsValue<T>(string key, T value)
        where T : notnull;

    /// <summary>
    /// Deletes a key value pair from the group.
    /// Fails if the group does not contain the key.
    /// </summary>
    Result DeleteValue(string key);

    /// <summary>
    /// Deletes all key value pairs from the group.
    /// </summary>
    void Reset();
}