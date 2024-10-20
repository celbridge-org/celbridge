namespace Celbridge.ResourceData;

public interface IResourceDataService
{
    /// <summary>
    /// Gets the value of a property from the "Properties" object in the root of the resource data.
    /// </summary>
    Result<T> GetProperty<T>(ResourceKey resource, string propertyName, T defaultValue = default(T)!) where T : notnull;

    /// <summary>
    /// Sets the value of a property in the "Properties" object in the root of the resource data.
    /// </summary>
    Result SetProperty<T>(ResourceKey resource, string propertyName, T newValue) where T : notnull;

    /// <summary>
    /// Registers a callback that gets triggered when a property in the resource is modified.
    /// </summary>
    void RegisterNotifier(ResourceKey resource, object recipient, Action<ResourceKey, string> callback);

    /// <summary>
    /// Unregisters a callback for the given resource key and recipient.
    /// </summary>
    void UnregisterNotifier(ResourceKey resource, object recipient);

    /// <summary>
    /// Remaps the old resource key to a new resource key.
    /// </summary>
    Result RemapResourceKey(ResourceKey oldResource, ResourceKey newResource);

    /// <summary>
    /// Saves all modified resources to disk asynchronously.
    /// </summary>
    Task<Result> SavePendingAsync();
}

