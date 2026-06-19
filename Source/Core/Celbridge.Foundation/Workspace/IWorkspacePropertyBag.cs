namespace Celbridge.Workspace;

/// <summary>
/// Dynamic key/value property bag for per-project data that does not fit a static
/// setting descriptor, such as per-extension editor preferences, per-resource
/// screenplay keys, document layout, and search history. Persisted alongside the
/// project files. Distinct from IWorkspaceSettings, the typed facade over the
/// Workspace-scope setting descriptors.
/// </summary>
public interface IWorkspacePropertyBag
{
    /// <summary>
    /// Gets the data version for the workspace property bag.
    /// </summary>
    Task<int> GetDataVersionAsync();

    /// <summary>
    /// Sets the data version for the workspace property bag.
    /// </summary>
    Task SetDataVersionAsync(int version);

    /// <summary>
    /// Sets a property of type T with the specified key.
    /// </summary>
    Task SetPropertyAsync<T>(string key, T value) where T : notnull;

    /// <summary>
    /// Gets the specified property as an object of type T.
    /// Returns defaultValue if the key was not found or if the property could not be deserialized to type T.
    /// </summary>
    Task<T?> GetPropertyAsync<T>(string key, T? defaultValue);

    /// <summary>
    /// Gets the specified property as an object of type T.
    /// Returns default(T) if the key was not found or if the property could not be deserialized to type T.
    /// </summary>
    Task<T?> GetPropertyAsync<T>(string key);

    /// <summary>
    /// Deletes the specified property.
    /// Returns true if the property existed prior to deletion.
    /// </summary>
    Task<bool> DeletePropertyAsync(string key);
}
