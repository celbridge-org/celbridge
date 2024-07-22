using Celbridge.Resources;

namespace Celbridge.Clipboard;

public interface IClipboardService
{
    /// <summary>
    /// Returns the current clipboard item's content type.
    /// </summary>
    ClipboardContentType GetClipboardContentType();

    /// <summary>
    /// Returns a description of the resource content on the clipboard.
    /// The destination resource keys are resolved relative to the specified destination folder.
    /// Fails if the current clipboard content type is not ClipboardContentType.Resource
    /// </summary>
    Task<Result<IClipboardResourceContent>> GetClipboardResourceContent(ResourceKey destFolderResource);

    /// <summary>
    /// Paste resources from the clipboard into the specified destination folder resource.
    /// Fails if the current clipboard content type is not ClipboardContentType.Resource
    /// </summary>
    Task<Result> PasteResourceItems(ResourceKey destFolderResource);
}
