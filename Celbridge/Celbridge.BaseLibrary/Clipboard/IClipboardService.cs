using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Clipboard;

public interface IClipboardService
{
    /// <summary>
    /// Returns the current clipboard item's content type.
    /// </summary>
    ClipboardContentType GetClipboardContentType();

    /// <summary>
    /// Returns a description of the resource items to be pasted for the current clipboard content.
    /// The destination resource keys are resolved relative to the specified destination folder.
    /// </summary>
    Task<Result<IClipboardResourcesDescription>> GetClipboardResourceDescription(ResourceKey destFolderResource);

    /// <summary>
    /// Paste resources from the clipboard into the specified destination folder resource.
    /// The call will fail if the current clipboard content is not a resource.
    /// </summary>
    Task<Result> PasteResourceItems(ResourceKey destFolderResource);
}
