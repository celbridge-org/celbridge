namespace Celbridge.Entities;

/// <summary>
/// Provides methods for initializing, retrieving, and manipulating entity data associated with resources.
/// </summary>
public interface IEntityService
{
    Task<Result> InitializeAsync();

    /// <summary>
    /// Gets the folder path where entities are stored.
    /// </summary>
    public string GetEntitiesFolderPath();

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
    /// Gets the value of a property from a resource.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Returns a default value if the property cannot be found.
    /// </summary>
    T? GetProperty<T>(ResourceKey resource, string propertyPath, T? defaultValue) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a resource.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Fails if the property cannot be found, or is of the wrong type.
    /// </summary>
    Result<T> GetProperty<T>(ResourceKey resource, string propertyPath) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a resource. The value is returned as a JSON encoded string.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Fails if the property cannot be found.
    /// </summary>
    Result<string> GetPropertyAsJSON(ResourceKey resource, string propertyPath);

    /// <summary>
    /// Sets the value of an entity property for a resource.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// </summary>
    Result<EntityPatchSummary> SetProperty<T>(ResourceKey resource, string propertyPath, T newValue) where T : notnull;

    /// <summary>
    /// Apply a JSON Patch (RFC 6902) to the Entity Data for a resource.
    /// </summary>
    Result<EntityPatchSummary> ApplyPatch(ResourceKey resource, string patch);

    /// <summary>
    /// Undo the most recently applied Entity Data patch for a resource.
    /// </summary>
    Result<bool> UndoPatch(ResourceKey resource);

    /// <summary>
    /// Redo the most recently applied Entity Data patch for a resource.
    /// </summary>
    Result<bool> RedoPatch(ResourceKey resource);

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
}

