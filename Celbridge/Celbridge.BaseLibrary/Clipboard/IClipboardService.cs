using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Clipboard;

public interface IClipboardService
{
    /// <summary>
    /// Returns the current clipboard item's content type.
    /// </summary>
    ClipboardContentType GetClipboardContentType();

    /// <summary>
    /// Pastes resources from the clipboard into the specified target resource.
    /// If the target resource is a file, the resources are pasted into the parent folder of the file.
    /// Fails if the current clipboard content is not a resource.
    /// </summary>
    Task<Result> PasteResources(ResourceKey FolderResource);
}
