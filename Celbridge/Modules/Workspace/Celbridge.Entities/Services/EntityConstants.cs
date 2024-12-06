namespace Celbridge.Entities.Services;

/// <summary>
/// Strings constants for entities and components.
/// </summary>
public static class EntityConstants
{
    /// <summary>
    /// Folder containing the component config files.
    /// </summary>
    public const string ComponentConfigFolder = "ComponentConfig";

    /// <summary>
    /// Folder containing the component schema definitions.
    /// </summary>
    public const string SchemasFolder = "Schemas";

    /// <summary>
    /// Folder containing the component prototype definitions.
    /// </summary>
    public const string PrototypesFolder = "Prototypes";

    /// <summary>
    /// JSON key for components array.
    /// </summary>
    public const string ComponentsKey = "_components";

    /// <summary>
    /// JSON key for component type string.
    /// </summary>
    public const string ComponentTypeKey = "_componentType";

    /// <summary>
    /// JSON key for allowMultipleComponents attribute.
    /// </summary>
    public const string AllowMultipleComponentsKey = "allowMultipleComponents";
}
