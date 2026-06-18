namespace Celbridge.Workspace;

/// <summary>
/// Synchronous key/value store backing Workspace-scope settings, persisted as a
/// JSON file in the project folder. Values are serialized to and from JSON by the
/// store; keys are stored verbatim. Writes update an in-memory copy synchronously
/// and are persisted to disk by FlushAsync, so reads see a write immediately but
/// the disk write is batched. The settings service reaches the live store through
/// the workspace at call time.
/// </summary>
public interface IWorkspaceSettingsStore
{
    /// <summary>
    /// Returns true and the deserialized value when the key has a stored value
    /// that reads back as T; false otherwise, leaving value at its default.
    /// </summary>
    bool TryGetValue<T>(string key, out T value) where T : notnull;

    /// <summary>
    /// Stores a value for the key in memory, replacing any existing value, and
    /// marks the store dirty. Call FlushAsync to persist.
    /// </summary>
    void SetValue<T>(string key, T value) where T : notnull;

    /// <summary>
    /// Returns true when the key has a stored value.
    /// </summary>
    bool ContainsKey(string key);

    /// <summary>
    /// Removes the stored value for the key in memory and marks the store dirty.
    /// Call FlushAsync to persist.
    /// </summary>
    void RemoveValue(string key);

    /// <summary>
    /// Writes pending in-memory changes to disk. A no-op when nothing has changed
    /// since the last write.
    /// </summary>
    Task FlushAsync();
}
