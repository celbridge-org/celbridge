namespace Celbridge.ResourceData;

public interface IResourceDataService
{
    /// <summary>
    /// Acquires the resource data for the given resource.
    /// If a resource data file already exists, it is loaded into memory, otherwise a new
    /// resource data instance is allocated.
    /// </summary>
    Result AcquireResourceData(ResourceKey resource);

    /// <summary>
    /// Gets the value at the specified JSON path for the given resource.
    /// </summary>
    Result<T> GetValue<T>(ResourceKey resource, string jsonPath) where T : notnull;

    /// <summary>
    /// Gets the value at the specified JSON path for the given resource.
    /// If the path is not found, then defaultValue is returned.
    /// </summary>
    Result<T> GetValue<T>(ResourceKey resource, string jsonPath, T defaultValue) where T : notnull;

    /// <summary>
    /// Sets the value at the specified JSON path for the given resource key.
    /// Automatically creates a new JSON file if it doesn't exist.
    /// </summary>
    public Result SetValue<T>(ResourceKey resource, string jsonPath, T newValue) where T : notnull;

    /// <summary>
    /// Updates all internal references from the old resource key to the new resource key.
    /// Also renames the backing JSON file.
    /// </summary>
    Result RemapResourceKey(ResourceKey oldResource, ResourceKey newResource);

    /// <summary>
    /// Saves all modified resources to disk asynchronously and clears the modified list.
    /// </summary>
    Task<Result> SavePendingAsync();

    /// <summary>
    /// Registers a callback that gets triggered when the resource data for the specified key is modified.
    /// Multiple listeners can register for the same resource, and each must pass an object.
    /// </summary>
    void RegisterNotifier(ResourceKey resourceKey, object recipient, Action<ResourceKey, string> callback);

    /// <summary>
    /// Unregisters a notifier callback for the given resource key based on the recipient object.
    /// </summary>
    void UnregisterNotifier(ResourceKey resourceKey, object recipient);
}
