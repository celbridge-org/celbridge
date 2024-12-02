namespace Celbridge.Entities;

/// <summary>
/// Provides methods for initializing, retrieving, and manipulating entity data associated with resources.
/// </summary>
public interface IEntityService
{
    /// <summary>
    /// Initializes the Entity Service.
    /// </summary>
    Task<Result> InitializeAsync();

    /// <summary>
    /// Returns the absolute path of the Entity Data file for a resource.
    /// The path will be generated regardless of whether the resource or Entity Data file actually exist.
    /// </summary>
    string GetEntityDataPath(ResourceKey resource);

    /// <summary>
    /// Returns the project relative path of the Entity Data file for a resource.
    /// </summary>
    string GetEntityDataRelativePath(ResourceKey resource);

    /// <summary>
    /// Saves all modified entities to disk asynchronously.
    /// </summary>
    Task<Result> SaveModifiedEntities();

    /// <summary>
    /// Move the entity data file for a resource, if one exists, to a new resource location.
    /// </summary>
    Result MoveEntityDataFile(ResourceKey sourceResource, ResourceKey destResource);

    /// <summary>
    /// Copy the entity data file for a resource, if one exists, to a new resource location.
    /// </summary>
    Result CopyEntityDataFile(ResourceKey sourceResource, ResourceKey destResource);

    /// <summary>
    /// Add a component to the the entity for a resource at the specified index.
    /// </summary>
    Result AddComponent(ResourceKey resource, string componentType, int componentIndex);

    /// <summary>
    /// Removes a component at the specified index from the entity for a resource.
    /// </summary>
    Result RemoveComponent(ResourceKey resource, int componentIndex);

    /// <summary>
    /// Gets the value of a property from a component.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Returns a default value if the property cannot be found.
    /// </summary>
    T? GetProperty<T>(ResourceKey resource, int componentIndex, string propertyPath, T? defaultValue) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a component.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Fails if the property cannot be found, or is of the wrong type.
    /// </summary>
    Result<T> GetProperty<T>(ResourceKey resource, int componentIndex, string propertyPath) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a component. The value is returned as a JSON encoded string.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Fails if the property cannot be found.
    /// </summary>
    Result<string> GetPropertyAsJSON(ResourceKey resource, int componentIndex, string propertyPath);

    /// <summary>
    /// Sets the value of an entity property for a component.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// </summary>
    Result SetProperty<T>(ResourceKey resource, int componentIndex, string propertyPath, T newValue) where T : notnull;

    /// <summary>
    /// Undo the most recent property change for a resource.
    /// </summary>
    Result<bool> UndoProperty(ResourceKey resource);

    /// <summary>
    /// Redo the most recently undone property change for a resource.
    /// </summary>
    Result<bool> RedoProperty(ResourceKey resource);
}

