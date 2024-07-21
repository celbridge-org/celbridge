using Celbridge.BaseLibrary.Project;

namespace Celbridge.BaseLibrary.Clipboard;

/// <summary>
/// Describes resource content on the clipboard.
/// </summary>
public interface IClipboardResourceContent
{
    /// <summary>
    /// Specifies whether the resources should be copied or moved when pasted.
    /// </summary>
    CopyResourceOperation Operation { get; }

    /// <summary>
    /// The resource items available to be pasted from the clipboard.
    /// </summary>
    List<ClipboardResourceItem> ResourceItems { get; }
}
