namespace Celbridge.Entities;

/// <summary>
/// Enum representing the type of change made to an entity property.
/// </summary>
public enum EntityPropertyChangeType
{
    Add,
    Update,
    Remove
}

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
    /// </summary>
    T? GetProperty<T>(ResourceKey resource, string propertyPath, T? defaultValue) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a resource.
    /// </summary>
    T? GetProperty<T>(ResourceKey resource, string propertyPath) where T : notnull;

    /// <summary>
    /// Sets the value of a property in a resource.
    /// </summary>
    void SetProperty<T>(ResourceKey resource, string propertyPath, T newValue) where T : notnull;

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

