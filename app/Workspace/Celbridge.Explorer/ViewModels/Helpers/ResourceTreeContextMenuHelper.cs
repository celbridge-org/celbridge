using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Python;

namespace Celbridge.Explorer.ViewModels.Helpers;

/// <summary>
/// Handles context menu state updates for the resource tree.
/// </summary>
public class ResourceTreeContextMenuHelper
{
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IDocumentsService _documentsService;
    private readonly IDataTransferService _dataTransferService;
    private readonly IPythonService _pythonService;

    public ResourceTreeContextMenuHelper(
        IResourceRegistry resourceRegistry,
        IDocumentsService documentsService,
        IDataTransferService dataTransferService,
        IPythonService pythonService)
    {
        _resourceRegistry = resourceRegistry;
        _documentsService = documentsService;
        _dataTransferService = dataTransferService;
        _pythonService = pythonService;
    }

    /// <summary>
    /// Checks if a resource can be opened as a document.
    /// </summary>
    public bool IsSupportedDocumentFormat(IResource? resource)
    {
        if (resource is IFileResource fileResource)
        {
            var resourceKey = _resourceRegistry.GetResourceKey(fileResource);
            return _documentsService.IsDocumentSupported(resourceKey);
        }
        return false;
    }

    /// <summary>
    /// Checks if a resource is an executable script.
    /// </summary>
    public bool IsResourceExecutable(IResource? resource)
    {
        if (resource is IFileResource fileResource)
        {
            var resourceKey = _resourceRegistry.GetResourceKey(fileResource);
            var extension = Path.GetExtension(resourceKey);

            if (extension == ExplorerConstants.PythonExtension ||
                extension == ExplorerConstants.IPythonExtension)
            {
                return _pythonService.IsPythonHostAvailable;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the clipboard contains resources that can be pasted.
    /// </summary>
    public async Task<bool> IsResourceOnClipboardAsync(IResource? destResource)
    {
        var contentDescription = _dataTransferService.GetClipboardContentDescription();

        if (contentDescription.ContentType != ClipboardContentType.Resource)
        {
            return false;
        }

        var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(destResource);
        var getResult = await _dataTransferService.GetClipboardResourceTransfer(destFolderResource);

        if (getResult.IsSuccess)
        {
            var content = getResult.Value;
            return content.TransferItems.Count > 0;
        }

        return false;
    }
}
