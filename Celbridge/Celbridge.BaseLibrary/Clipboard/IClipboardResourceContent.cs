using Celbridge.BaseLibrary.Project;

namespace Celbridge.BaseLibrary.Clipboard;

/// <summary>
/// Describes the resource items that are available for pasting from the clipboard.
/// </summary>
public interface IClipboardResourceContent
{
    /// <summary>
    /// Specifies whether the resource items should be copied or moved when pasted.
    /// </summary>
    CopyResourceOperation Operation { get; }

    /// <summary>
    /// Resource items available to be pasted from the clipboard.
    /// </summary>
    List<ClipboardResourceItem> ResourceItems { get; }
}
