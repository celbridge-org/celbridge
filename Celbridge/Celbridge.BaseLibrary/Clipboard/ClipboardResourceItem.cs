using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Clipboard;

/// <summary>
/// A clipboard resource item.
/// </summary>
public record ClipboardResourceItem
(
    /// <summary>
    /// The type of the resource.
    /// </summary>
    ResourceType ResourceType,

    /// <summary>
    /// The absolute path of the resource being copied or moved.
    /// </summary>
    string SourcePath,

    /// <summary>
    /// The resource at the source location.
    /// This property is only populated for resources that are inside the project folder.
    /// Resources that are outside the project folder are assigned an empty SourceResource field.
    /// </summary>
    ResourceKey SourceResource,

    /// <summary>
    /// The key representing the resource at the destination location.
    /// It is valid for the source and dest resource to be the same, indicating a duplicate resource operation.
    /// </summary>
    ResourceKey DestResource
);
