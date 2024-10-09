using Celbridge.Explorer;
using Celbridge.Foundation;

namespace Celbridge.DataTransfer;

/// <summary>
/// Services to support data transfer operations, such as cut, copy, paste + drag and drop.
/// </summary>
public interface IDataTransferService
{
    /// <summary>
    /// Returns a description of the current content on the clipboard.
    /// </summary>
    ClipboardContentDescription GetClipboardContentDescription();

    /// <summary>
    /// Returns a resource transfer data structure describing the resource content on the clipboard.
    /// The destination resource keys are resolved relative to the specified destination folder.
    /// Fails if the current clipboard content type is not ClipboardContentType.Resource
    /// </summary>
    Task<Result<IResourceTransfer>> GetClipboardResourceTransfer(ResourceKey destFolderResource);

    /// <summary>
    /// Paste resources from the clipboard into the specified destination folder resource.
    /// Fails if the current clipboard content type is not ClipboardContentType.Resource
    /// </summary>
    Task<Result> PasteClipboardResources(ResourceKey destFolderResource);
}
