namespace Celbridge.ResourceData;

/// <summary>
/// Represents a callback method that is triggered when a property in a resource is modified.
/// The callback provides the <see cref="ResourceKey"/> of the modified resource and the name of the modified property.
/// </summary>
/// <param name="resource">The resource that was modified.</param>
/// <param name="propertyName">The name of the property that was modified.</param>
public delegate void ResourcePropertyChangedNotifier(ResourceKey resource, string propertyName);

public interface IResourceDataService
{
    /// <summary>
    /// Gets the value of a property from the "Properties" object in the root of the resource data.
    /// If the property is not found, defaultValue is returned instead.
    /// Throws an invalid operation exception if an error occurs while getting the property.
    /// </summary>
    T? GetProperty<T>(ResourceKey resource, string propertyName, T? defaultValue) where T : notnull;

    /// <summary>
    /// Gets the value of a property from the "Properties" object in the root of the resource data.
    /// If the property is not found, default(T) is returned instead.
    /// Throws an invalid operation exception if an error occurs while getting the property.
    /// </summary>
    T? GetProperty<T>(ResourceKey resource, string propertyName) where T : notnull;

    /// <summary>
    /// Sets the value of a property in the "Properties" object in the root of the resource data.
    /// Throws an invalid operation exception if an error occurs while setting the property.
    /// </summary>
    void SetProperty<T>(ResourceKey resource, string propertyName, T newValue) where T : notnull;

    /// <summary>
    /// Registers a callback that gets triggered when a property in the resource is modified.
    /// </summary>
    void RegisterNotifier(ResourceKey resource, object recipient, ResourcePropertyChangedNotifier notifier);

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

