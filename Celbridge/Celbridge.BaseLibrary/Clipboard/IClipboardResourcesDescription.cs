using Celbridge.BaseLibrary.Project;

namespace Celbridge.BaseLibrary.Clipboard;

/// <summary>
/// Describes the resource items that are available for pasting from the clipboard.
/// </summary>
public interface IClipboardResourcesDescription
{
    /// <summary>
    /// Specifies whether the resource items should be copied or moved when pasting.
    /// </summary>
    CopyResourceOperation Operation { get; }

    /// <summary>
    /// Resource items that may be pasted from the clipboard.
    /// Resources that are outside the project folder have an empty SourceResource field.
    /// </summary>
    List<ClipboardResourceItem> ResourceItems { get; }
}
