using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Messaging;
using Celbridge.Resources;

using ApplicationDataTransfer = Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Workspace.Services;

public class DataTransferService : IDataTransferService, IDisposable
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public DataTransferService(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;

        ApplicationDataTransfer.Clipboard.ContentChanged += Clipboard_ContentChanged;
    }

    private void Clipboard_ContentChanged(object? sender, object e)
    {
        var message = new ClipboardContentChangedMessage();
        _messengerService.Send(message);
    }

    public ClipboardContentDescription GetClipboardContentDescription()
    {
        var dataPackageView = ApplicationDataTransfer.Clipboard.GetContent();

        ClipboardContentType contentType;
        if (dataPackageView.Contains(ApplicationDataTransfer.StandardDataFormats.StorageItems))
        {
            contentType =  ClipboardContentType.Resource;
        }
        else if (dataPackageView.Contains(ApplicationDataTransfer.StandardDataFormats.Text))
        {
            contentType =  ClipboardContentType.Text;
        }
        else
        {
            contentType = ClipboardContentType.None;
        }

        ClipboardContentOperation contentOperation;
        if (contentType != ClipboardContentType.None)
        {
            switch (dataPackageView.RequestedOperation)
            {
                case ApplicationDataTransfer.DataPackageOperation.None:
                default:
                    contentOperation = ClipboardContentOperation.None;
                    break;
                case ApplicationDataTransfer.DataPackageOperation.Copy:
                    contentOperation = ClipboardContentOperation.Copy;
                    break;
                case ApplicationDataTransfer.DataPackageOperation.Move:
                    contentOperation = ClipboardContentOperation.Move;
                    break;
            }

            return new ClipboardContentDescription(contentType, contentOperation);
        }

        return new ClipboardContentDescription(ClipboardContentType.None, ClipboardContentOperation.None);
    }

    public async Task<Result<IResourceTransfer>> GetClipboardResourceTransfer(ResourceKey destFolderResource)
    {
        var contentDescription = GetClipboardContentDescription();

        if (contentDescription.ContentType != ClipboardContentType.Resource)
        {
            return Result<IResourceTransfer>.Fail("Clipboard content does not contain a resource");
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result<IResourceTransfer>.Fail("Workspace is not loaded");
        }

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceRegistry = resourceService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(destFolderResource);
        if (getResult.IsFailure)
        {
            return Result<IResourceTransfer>.Fail($"Destination folder resource '{destFolderResource}' does not exist");
        }

        var resource = getResult.Value;
        if (resource is not IFolderResource)
        {
            return Result<IResourceTransfer>.Fail($"Resource '{destFolderResource}' is not a folder resource");
        }

        var destFolderPath = resourceRegistry.GetResourcePath(resource);
        if (!Directory.Exists(destFolderPath))
        {
            return Result<IResourceTransfer>.Fail($"The path '{destFolderPath}' does not exist.");
        }

        var dataPackageView = ApplicationDataTransfer.Clipboard.GetContent();

        try
        {
            var storageItems = await dataPackageView.GetStorageItemsAsync();
            var paths = new List<string>();
            foreach (var storageItem in storageItems)
            {
                var path = Path.GetFullPath(storageItem.Path);
                paths.Add(path);
            }

            // Note whether the operation is a move or a copy
            var transferMode =
                contentDescription.ContentOperation == ClipboardContentOperation.Move
                ? DataTransferMode.Move
                : DataTransferMode.Copy;

            var createTransferResult = resourceService.CreateResourceTransfer(paths, destFolderResource, transferMode);
            if (createTransferResult.IsFailure)
            {
                var failure = Result<IResourceTransfer>.Fail($"Failed to create resource transfer.");
                failure.MergeErrors(createTransferResult);
                return failure;
            }
            var resourceTransfer = createTransferResult.Value;

            return Result<IResourceTransfer>.Ok(resourceTransfer);
        }
        catch (Exception ex)
        {
            return Result<IResourceTransfer>.Fail($"Failed to generate clipboard resource description. {ex}");
        }
    }

    public async Task<Result> PasteClipboardResources(ResourceKey destFolderResource)
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to paste resource items because no workspace is loaded");
        }

        var getResult = await GetClipboardResourceTransfer(destFolderResource);
        if (getResult.IsFailure)
        {
            var failure = Result.Fail("Failed to get clipboard resource transfer");
            failure.MergeErrors(getResult);
            return failure;
        }
        var description = getResult.Value;

        if (description.TransferItems.Count == 1 &&
            description.TransferMode == DataTransferMode.Copy)
        {
            // If the source and destination resource are the same, display the duplicate
            // resource dialog instead of pasting the item.
            var clipboardResource = description.TransferItems[0]!;
            if (clipboardResource.SourceResource == clipboardResource.DestResource)
            {
                _commandService.Execute<IDuplicateResourceDialogCommand>(command =>
                {
                    command.Resource = clipboardResource.SourceResource;
                });
                return Result.Ok();
            }
        }

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        return await resourceService.TransferResources(destFolderResource, description);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
                ApplicationDataTransfer.Clipboard.ContentChanged -= Clipboard_ContentChanged;
            }

            _disposed = true;
        }
    }

    ~DataTransferService()
    {
        Dispose(false);
    }
}
