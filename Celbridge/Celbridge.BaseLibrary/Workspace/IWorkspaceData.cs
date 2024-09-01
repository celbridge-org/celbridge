namespace Celbridge.Workspace;

/// <summary>
/// Manages the workspace data associated with a loaded project.
/// </summary>
public interface IWorkspaceData
{
    /// <summary>
    /// Gets the data version for the workspace data.
    /// </summary>
    Task<int> GetDataVersionAsync();

    /// <summary>
    /// Sets the data version for the workspace data.
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
}
