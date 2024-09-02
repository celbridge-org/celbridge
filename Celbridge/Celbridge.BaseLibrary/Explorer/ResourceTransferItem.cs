namespace Celbridge.Explorer;

/// <summary>
/// Describes the transfer of resource from one location to another.
/// </summary>
public record ResourceTransferItem
(
    /// <summary>
    /// The type of the resource.
    /// </summary>
    ResourceType ResourceType,

    /// <summary>
    /// The absolute path of the resource at the source location.
    /// </summary>
    string SourcePath,

    /// <summary>
    /// The key representing the resource at the source location.
    /// This property is only populated for resources that are within the project folder.
    /// Resources that are outside the project folder are assigned an empty resource key.
    /// </summary>
    ResourceKey SourceResource,

    /// <summary>
    /// The key representing the resource at the destination location.
    /// The SourceResource and DestResource may have the same value, indicating a duplicate resource operation.
    /// </summary>
    ResourceKey DestResource
);
