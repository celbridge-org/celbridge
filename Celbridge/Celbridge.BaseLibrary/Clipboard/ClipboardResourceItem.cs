using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Clipboard;

/// <summary>
/// A clipboard resource item.
/// </summary>
public record ClipboardResourceItem
(
    /// <summary>
    /// The type of the resource (file, folder).
    /// </summary>
    ResourceType ResourceType,

    /// <summary>
    /// The source path of the resource being copied or moved.
    /// </summary>
    string SourcePath,

    /// <summary>
    /// The key representing the resource at the source location.
    /// </summary>
    ResourceKey SourceResource,

    /// <summary>
    /// The key representing the resource at the destination location.
    /// </summary>
    ResourceKey DestResource
);
