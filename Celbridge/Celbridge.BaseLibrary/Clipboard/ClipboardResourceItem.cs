using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Clipboard;

/// <summary>
/// A clipboard resource item.
/// </summary>
public record ClipboardResourceItem
(
    ResourceType ResourceType, 
    string SourcePath, 
    ResourceKey SourceResource, 
    ResourceKey DestResource
);
