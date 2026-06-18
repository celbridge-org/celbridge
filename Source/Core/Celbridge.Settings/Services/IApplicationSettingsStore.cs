namespace Celbridge.Settings.Services;

/// <summary>
/// Synchronous key/value store for user- and installation-level settings. Backs
/// the Application scope and holds the ciphertext of the Protected scope. One
/// implementation per platform: Windows LocalSettings when packaged, an in-memory
/// store otherwise. Keys are stored verbatim.
/// </summary>
internal interface IApplicationSettingsStore
{
    /// <summary>
    /// Gets the stored value for the key, or defaultValue when the key is absent
    /// or its value cannot be deserialized to T.
    /// </summary>
    T GetValue<T>(string key, T defaultValue) where T : notnull;

    /// <summary>
    /// Stores a value for the key, replacing any existing value.
    /// </summary>
    void SetValue<T>(string key, T value) where T : notnull;

    /// <summary>
    /// Returns true when the key has a stored value.
    /// </summary>
    bool ContainsKey(string key);

    /// <summary>
    /// Removes the stored value for the key. A no-op when the key is absent.
    /// </summary>
    void RemoveValue(string key);
}
