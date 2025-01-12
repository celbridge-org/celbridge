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
    /// A path will be generated regardless of whether the resource or Entity Data file actually exist.
    /// </summary>
    string GetEntityDataPath(ResourceKey resource);

    /// <summary>
    /// Returns the project relative path of the Entity Data file for a resource.
    /// </summary>
    string GetEntityDataRelativePath(ResourceKey resource);

    /// <summary>
    /// Saves all modified entities to disk asynchronously.
    /// </summary>
    Task<Result> SaveEntitiesAsync();

    /// <summary>
    /// Move the entity data file for a resource, if one exists, to a new resource location.
    /// </summary>
    Result MoveEntityDataFile(ResourceKey sourceResource, ResourceKey destResource);

    /// <summary>
    /// Copy the Entity Data file for a resource, if one exists, to a new resource location.
    /// </summary>
    Result CopyEntityDataFile(ResourceKey sourceResource, ResourceKey destResource);

    /// <summary>
    /// Add a component to the the entity for a resource at the specified index.
    /// </summary>
    Result AddComponent(ComponentKey componentKey, string componentType);

    /// <summary>
    /// Removes a component at the specified index from the entity for a resource.
    /// </summary>
    Result RemoveComponent(ComponentKey componentKey);

    /// <summary>
    /// Replace the entity component for a resource at the specified index.
    /// </summary>
    Result ReplaceComponent(ComponentKey componentKey, string componentType);

    /// <summary>
    /// Copy an entity component from a source index to a destination index.
    /// </summary>
    Result CopyComponent(ResourceKey resource, int sourceComponentIndex, int destComponentIndex);

    /// <summary>
    /// Move an entity component from a source index to a destination index.
    /// </summary>
    Result MoveComponent(ResourceKey resource, int sourceComponentIndex, int destComponentIndex);

    /// <summary>
    /// Returns the number of components in the entity for a resource.
    /// Returns 0 if the lookup fails for any reason (e.g. the resource does not exist).
    /// </summary>
    int GetComponentCount(ResourceKey resource);

    /// <summary>
    /// Returns the component type for the entity component at the specified index.
    /// </summary>
    Result<string> GetComponentType(ComponentKey componentKey);

    /// <summary>
    /// Returns the ComponentSchema for a component type.
    /// </summary>
    Result<ComponentSchema> GetComponentSchema(string componentType);

    /// <summary>
    /// Returns the component at the specified index in the entity.
    /// </summary>
    Result<IComponentProxy> GetComponent(ComponentKey componentKey);

    /// <summary>
    /// Returns the first component of the specified type in the entity.
    /// </summary>
    Result<IComponentProxy> GetComponentOfType(ResourceKey resource, string componentType);

    /// <summary>
    /// Returns all components in the entity.
    /// </summary>
    Result<IReadOnlyList<IComponentProxy>> GetComponents(ResourceKey resource);

    /// <summary>
    /// Returns all components of the specified type in the entity.
    /// </summary>
    Result<IReadOnlyList<IComponentProxy>> GetComponentsOfType(ResourceKey resource, string componentType);

    /// <summary>
    /// Gets the value of a property from a component.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Returns the default value if the component or property cannot be found.
    /// </summary>
    T? GetProperty<T>(ComponentKey componentKey, string propertyPath, T? defaultValue) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a component.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Fails if the property cannot be found, or is of the wrong type.
    /// </summary>
    Result<T> GetProperty<T>(ComponentKey componentKey, string propertyPath) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a component, returned as a JSON encoded string.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Fails if the property cannot be found.
    /// </summary>
    Result<string> GetPropertyAsJson(ComponentKey componentKey, string propertyPath);

    /// <summary>
    /// Replaces the value of an existing entity property for a component.
    /// If insert is true then the value is inserted at the specified key/index, rather than replacing the existing entry.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// </summary>
    Result SetProperty<T>(ComponentKey componentKey, string propertyPath, T newValue, bool insert = false) where T : notnull;

    /// <summary>
    /// Returns the number of available undo operations for an entity.
    /// </summary>
    int GetUndoCount(ResourceKey resource);

    /// <summary>
    /// Undo the most recent entity change for a resource.
    /// </summary>
    Result UndoEntity(ResourceKey resource);

    /// <summary>
    /// Returns the number of available redo operations for an entity.
    /// </summary>
    int GetRedoCount(ResourceKey resource);

    /// <summary>
    /// Redo the most recently undone entity change for a resource.
    /// </summary>
    Result RedoEntity(ResourceKey resource);

    /// <summary>
    /// Returns true if any component in the entity has the specified tag.
    /// </summary>
    bool HasTag(ResourceKey resource, string tag);

    /// <summary>
    /// Returns the list of all available component types.
    /// </summary>
    List<string> GetAllComponentTypes();

    /// <summary>
    /// Returns the activity property for the entity.
    /// </summary>
    Result<string> GetActivity(ResourceKey resource);

    /// <summary>
    /// Sets the activity property for the entity.
    /// This will trigger an entity save if the activity is different from the current value.
    /// </summary>
    Result SetActivity(ResourceKey resource, string activity);
}
