namespace Celbridge.Settings;

/// <summary>
/// Synchronous, typed key/value store backing one settings scope. Writes are
/// deferred and persisted by FlushAsync.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Returns true and the stored value when the key has a value that reads back
    /// as T; false otherwise, leaving value at its default.
    /// </summary>
    bool TryGetValue<T>(string key, out T value) where T : notnull;

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

    /// <summary>
    /// Writes any pending in-memory changes to the backing store, reporting a
    /// failed Result if the write fails. A no-op when nothing has changed.
    /// </summary>
    Task<Result> FlushAsync();
}
